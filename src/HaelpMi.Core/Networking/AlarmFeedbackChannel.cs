using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Networking;

/// <summary>
/// TCP channel for post-trigger alarm feedback (FR-51/FR-53): "bin unterwegs" flowing
/// recipient -> sender, and the aggregated status relay flowing sender -> every
/// recipient. Every device plays both roles depending on which alarm it's currently
/// part of, so - same pattern as <see cref="EditLockService"/> - one class listens and
/// sends. Messages are multiplexed over one port via <see cref="AlarmFeedbackEnvelope"/>.
///
/// Primary/Satellite (Issue #9-Nachtrag 2, identisches Muster wie <see cref="AlarmChannel"/>
/// für den Alarm-Empfang): der TCP-Port lässt sich maschinenweit nur einmal binden. Ohne
/// diese Rolle wäre bei Fast User Switching nur die Sitzung erreichbar, die den Port
/// zufällig zuerst gebunden hat - eine Antwort ("bin unterwegs") an eine ANDERE, gerade
/// tatsächlich sendende Sitzung dieser Maschine würde dort nie ankommen und die wartende
/// <c>RepeatingAlarmSession</c> pingt endlos weiter, obwohl längst geantwortet wurde.
/// </summary>
public sealed class AlarmFeedbackChannel : IAsyncDisposable
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Action<string>? _audit;

    private TcpListener? _listener;
    private CancellationTokenSource? _acceptCts;
    private Task? _acceptLoop;

    private AlarmFeedbackRelayServer? _relayServer;
    private AlarmFeedbackRelayClient? _relayClient;
    private CancellationTokenSource? _satelliteCts;
    private Task? _satelliteLoop;

    public event EventHandler<AlarmOnMyWayMessage>? OnMyWayReceived;
    public event EventHandler<AlarmStatusRelayMessage>? StatusRelayReceived;

    public bool IsPrimary { get; private set; }

    public AlarmFeedbackChannel(Func<LiveIdentity> identityProvider, Action<string>? audit = null)
    {
        _identityProvider = identityProvider;
        _audit = audit;
    }

    public void Start(int port = AppConstants.AlarmFeedbackTcpPort)
    {
        if (_listener is not null || _satelliteLoop is not null)
        {
            return;
        }

        if (TryBindListener(port))
        {
            PromoteToPrimary();
            return;
        }

        _satelliteCts = new CancellationTokenSource();
        _satelliteLoop = RunAsSatelliteAsync(port, _satelliteCts.Token);
    }

    /// <returns>true, wenn der Port erfolgreich gebunden wurde (gleiches Muster wie <see cref="AlarmTcpListener.Start"/>).</returns>
    private bool TryBindListener(int port)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            return true;
        }
        catch (SocketException)
        {
            // Normalfall für jede Sitzung außer der zuerst gestarteten Primary bei Fast
            // User Switching (siehe Klassenkommentar) - kein Fehler, AlarmFeedbackChannel
            // fängt dieses false ab und verbindet stattdessen als Satellite.
            _listener = null;
            return false;
        }
    }

    private void PromoteToPrimary()
    {
        IsPrimary = true;
        _acceptCts = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(_listener!, _acceptCts.Token);

        _relayServer = new AlarmFeedbackRelayServer();
        _relayServer.Start();
        _audit?.Invoke("Alarm-Feedback-Primary-Instanz aktiv (hält den TCP-Port, reicht an Satellites weiter).");
    }

    private async Task RunAsSatelliteAsync(int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var client = new AlarmFeedbackRelayClient();
            client.EnvelopeReceived += OnRelayedEnvelopeReceived;

            var disconnected = new TaskCompletionSource();
            client.Disconnected += (_, _) => disconnected.TrySetResult();

            var connected = await client.TryConnectAsync(TimeSpan.FromSeconds(5), ct);
            if (connected)
            {
                _relayClient = client;
                _audit?.Invoke("Alarm-Feedback-Satellite-Instanz verbunden, empfängt Rückmeldungen über lokalen Relay-Kanal.");

                using var reg = ct.Register(() => disconnected.TrySetCanceled());
                try
                {
                    await disconnected.Task;
                }
                catch (OperationCanceledException)
                {
                    return; // regulärer Shutdown
                }
                finally
                {
                    _relayClient = null;
                }

                _audit?.Invoke("Verbindung zur Alarm-Feedback-Primary-Instanz verloren - versuche Übernahme.");
            }

            client.EnvelopeReceived -= OnRelayedEnvelopeReceived;

            if (TryBindListener(port))
            {
                PromoteToPrimary();
                return;
            }

            // Weder Primary erreichbar noch selbst Bind erfolgreich - zufälliger Backoff
            // wie beim Edit-Lock-Kollisionsfall (AppConstants.EditLockCollisionBackoff),
            // dann erneut versuchen.
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(500, 2000)), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnRelayedEnvelopeReceived(object? sender, AlarmFeedbackEnvelope envelope) => DispatchEnvelope(envelope);

    /// <summary>Recipient -> sender, single addressed connection (FR-51).</summary>
    public Task SendOnMyWayAsync(DeviceEntry senderDevice, AlarmOnMyWayMessage message, CancellationToken ct = default) =>
        SendEnvelopeAsync(senderDevice, AlarmFeedbackMessageType.OnMyWay, NetworkSerializer.ToJsonLine(message), ct);

    /// <summary>Sender -> every recipient, fire-and-forget in parallel, no response expected (FR-53).</summary>
    public async Task RelayStatusAsync(IReadOnlyList<DeviceEntry> recipients, AlarmStatusRelayMessage message, CancellationToken ct = default)
    {
        var payload = NetworkSerializer.ToJsonLine(message);
        await Task.WhenAll(recipients.Select(r => SendEnvelopeAsync(r, AlarmFeedbackMessageType.StatusRelay, payload, ct)));
    }

    private static async Task SendEnvelopeAsync(DeviceEntry target, AlarmFeedbackMessageType type, string payloadJson, CancellationToken ct)
    {
        try
        {
            if (!IPAddress.TryParse(target.IpAddress, out var address))
            {
                return;
            }

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            await client.ConnectAsync(address, AppConstants.AlarmFeedbackTcpPort, timeoutCts.Token);
            await using var stream = client.GetStream();

            var envelope = new AlarmFeedbackEnvelope(type, payloadJson);
            var bytes = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(envelope));
            await stream.WriteAsync(bytes, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);
        }
        catch (Exception)
        {
            // Best-effort (5.6 known challenge pattern): an unreachable target here just
            // means it doesn't get this particular status update / on-my-way notice.
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            _ = HandleConnectionAsync(client, ct);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 5000;
            await using var stream = client.GetStream();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            AlarmFeedbackEnvelope? envelope;
            try
            {
                envelope = NetworkSerializer.FromJsonLine<AlarmFeedbackEnvelope>(line);
            }
            catch (Exception)
            {
                return;
            }

            if (envelope is null)
            {
                return;
            }

            DispatchEnvelope(envelope);

            // An verbundene Satellite-Instanzen dieser Maschine weiterreichen (Issue #9-
            // Nachtrag 2) - unabhängig vom Kundengruppen-Ergebnis oben, DispatchEnvelope
            // prüft das für jede Sitzung ohnehin nochmal einzeln (dieselbe Installation,
            // dasselbe Ergebnis überall, kein zusätzlicher Filterbedarf hier).
            if (_relayServer is not null)
            {
                await _relayServer.BroadcastAsync(envelope, ct);
            }
        }
        catch (Exception)
        {
            // best-effort - see class remarks
        }
    }

    /// <summary>
    /// Gemeinsamer Verarbeitungspfad für ein empfangenes <see cref="AlarmFeedbackEnvelope"/>,
    /// egal ob es gerade frisch über die eigene TCP-Verbindung (Primary) oder über den
    /// lokalen Relay-Kanal von der Primary-Instanz (Satellite, siehe
    /// <see cref="OnRelayedEnvelopeReceived"/>) hereingekommen ist - eigenes try/catch pro
    /// Payload, da dieser Pfad (anders als der TCP-Empfang) nicht mehr innerhalb des
    /// äußeren try/catch von <see cref="HandleConnectionAsync"/> läuft.
    /// </summary>
    private void DispatchEnvelope(AlarmFeedbackEnvelope envelope)
    {
        var identity = _identityProvider();

        switch (envelope.Type)
        {
            case AlarmFeedbackMessageType.OnMyWay:
                if (TryDeserialize<AlarmOnMyWayMessage>(envelope.PayloadJson) is { } onMyWay
                    && CustomerGroupFilter.Matches(onMyWay.CustomerGroupId, identity.CustomerGroupId))
                {
                    _audit?.Invoke($"onmyway received from deviceId={onMyWay.ResponderDeviceId}");
                    OnMyWayReceived?.Invoke(this, onMyWay);
                }
                break;

            case AlarmFeedbackMessageType.StatusRelay:
                if (TryDeserialize<AlarmStatusRelayMessage>(envelope.PayloadJson) is { } relay
                    && CustomerGroupFilter.Matches(relay.CustomerGroupId, identity.CustomerGroupId))
                {
                    StatusRelayReceived?.Invoke(this, relay);
                }
                break;
        }
    }

    private static T? TryDeserialize<T>(string payloadJson) where T : class
    {
        try
        {
            return NetworkSerializer.FromJsonLine<T>(payloadJson);
        }
        catch (Exception)
        {
            return null; // ein einzelner unlesbarer Payload darf den Kanal nicht stören
        }
    }

    public async ValueTask DisposeAsync()
    {
        _acceptCts?.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* handled inside loop */ }
        }
        _acceptCts?.Dispose();

        if (_relayServer is not null)
        {
            await _relayServer.DisposeAsync();
        }

        _satelliteCts?.Cancel();
        if (_satelliteLoop is not null)
        {
            try { await _satelliteLoop; } catch { /* already handled inside the loop */ }
        }
        _satelliteCts?.Dispose();

        if (_relayClient is not null)
        {
            await _relayClient.DisposeAsync();
        }
    }
}
