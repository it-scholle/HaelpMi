using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Security;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Networking;

/// <summary>
/// TCP channel for post-trigger alarm feedback (FR-51/FR-53): "bin unterwegs" flowing
/// recipient -> sender, and the aggregated status relay flowing sender -> every
/// recipient. Every device plays both roles depending on which alarm it's currently
/// part of, so - same pattern as <see cref="EditLockService"/> - one class listens and
/// sends. Messages are multiplexed over one port via <see cref="AlarmFeedbackEnvelope"/>.
/// </summary>
public sealed class AlarmFeedbackChannel : IAsyncDisposable
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Action<string>? _audit;
    private readonly Func<string?>? _groupKeyProvider;
    private readonly DeviceStore _deviceStore = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public event EventHandler<AlarmOnMyWayMessage>? OnMyWayReceived;
    public event EventHandler<AlarmStatusRelayMessage>? StatusRelayReceived;

    /// <param name="groupKeyProvider">Siehe AlarmSender-Konstruktor - gleiche Bedeutung, LAN-Verschlüsselung des Antwort-Kanals.</param>
    public AlarmFeedbackChannel(Func<LiveIdentity> identityProvider, Action<string>? audit = null, Func<string?>? groupKeyProvider = null)
    {
        _identityProvider = identityProvider;
        _audit = audit;
        _groupKeyProvider = groupKeyProvider;
    }

    public void Start(int port = AppConstants.AlarmFeedbackTcpPort)
    {
        if (_listener is not null)
        {
            return;
        }

        // Entdeckt 05.08.2026: ein SocketException hier (Port schon belegt, z. B. durch
        // eine noch laufende zweite Agent-Instanz nach einem VM-Klon-Test) riss bisher
        // ungefangen bis in StartBackgroundServices() durch und brach ALLES Weitere ab -
        // DiscoveryService, AlarmTcpListener, ConfigSyncService, Update-Verteilung liefen
        // dann gar nicht erst, obwohl sie mit diesem einen Port nichts zu tun haben. Gleiches
        // Graceful-Fallback-Muster wie ConfigSyncService.Start(): Senden (SendOnMyWayAsync/
        // RelayStatusAsync) baut ohnehin für jede Nachricht eine eigene ausgehende
        // Verbindung auf und bleibt unabhängig vom Listener funktionsfähig - nur das
        // EMPFANGEN von Feedback dieses Geräts fällt in diesem Fall aus, nicht der ganze Agent.
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _acceptLoop = AcceptLoopAsync(_listener, _cts.Token);
        }
        catch (SocketException)
        {
            _listener = null;
        }
    }

    /// <summary>Recipient -> sender, single addressed connection (FR-51).</summary>
    public Task SendOnMyWayAsync(DeviceEntry senderDevice, AlarmOnMyWayMessage message, CancellationToken ct = default) =>
        SendEnvelopeAsync(senderDevice, AlarmFeedbackMessageType.OnMyWay, NetworkSerializer.ToJsonLine(message), ct);

    /// <summary>Sender -> every recipient, fire-and-forget in parallel, no response expected (FR-53).</summary>
    public async Task RelayStatusAsync(IReadOnlyList<DeviceEntry> recipients, AlarmStatusRelayMessage message, CancellationToken ct = default)
    {
        var payload = NetworkSerializer.ToJsonLine(message);
        await Task.WhenAll(recipients.Select(r => SendEnvelopeAsync(r, AlarmFeedbackMessageType.StatusRelay, payload, ct)));
    }

    private async Task SendEnvelopeAsync(DeviceEntry target, AlarmFeedbackMessageType type, string payloadJson, CancellationToken ct)
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

            var innerEnvelope = new AlarmFeedbackEnvelope(type, payloadJson);

            // LAN-Verschlüsselung (CLAUDE.md "Lizenz & Secrets"): gleiche Fallback-Logik
            // wie AlarmSender - AlarmFeedbackEnvelope wird als Ganzes in ein SecureEnvelope
            // gepackt, wenn wir einen Gruppenschlüssel haben und das Zielgerät als
            // verschlüsselungsfähig+gepinnt bekannt ist, sonst unverändertes Klartextformat.
            var groupKeyBase64 = _groupKeyProvider?.Invoke();
            var canEncrypt = groupKeyBase64 is not null
                && target.ProtocolVersion is >= AppConstants.CurrentProtocolVersion
                && !string.IsNullOrEmpty(target.PinnedDeviceIdentityPublicKeyBase64);

            string line;
            if (canEncrypt)
            {
                var identity = _identityProvider();
                var devicePrivateKeyBase64 = DeviceIdentityStore.LoadOrCreate().PrivateKeyBase64;
                var secureEnvelope = SecureEnvelopeCodec.Seal(innerEnvelope, identity.CustomerGroupId, identity.DeviceId, groupKeyBase64, devicePrivateKeyBase64, DateTimeOffset.UtcNow);
                line = secureEnvelope is not null ? NetworkSerializer.ToJsonLine(secureEnvelope) : NetworkSerializer.ToJsonLine(innerEnvelope);
            }
            else
            {
                line = NetworkSerializer.ToJsonLine(innerEnvelope);
            }

            var bytes = NetworkSerializer.Encoding.GetBytes(line);
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

            var identity = _identityProvider();

            AlarmFeedbackEnvelope? envelope;
            if (SecureEnvelopeCodec.TryParse(line, out var secureEnvelope) && secureEnvelope is not null)
            {
                if (!CustomerGroupFilter.Matches(secureEnvelope.CustomerGroupId, identity.CustomerGroupId) || !secureEnvelope.IsPlausible())
                {
                    return;
                }

                var senderPinnedKey = _deviceStore.Load().FirstOrDefault(d => d.DeviceId == secureEnvelope.DeviceId)?.PinnedDeviceIdentityPublicKeyBase64;
                var groupKeyBase64 = _groupKeyProvider?.Invoke();
                envelope = SecureEnvelopeCodec.TryOpen<AlarmFeedbackEnvelope>(secureEnvelope, groupKeyBase64, senderPinnedKey, DateTimeOffset.UtcNow);
                if (envelope is null)
                {
                    return; // falscher Gruppenschlüssel/manipuliert/falscher Absender-Schlüssel - stiller Drop
                }
            }
            else
            {
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
            }

            switch (envelope.Type)
            {
                case AlarmFeedbackMessageType.OnMyWay:
                    var onMyWay = NetworkSerializer.FromJsonLine<AlarmOnMyWayMessage>(envelope.PayloadJson);
                    if (onMyWay is not null && CustomerGroupFilter.Matches(onMyWay.CustomerGroupId, identity.CustomerGroupId))
                    {
                        _audit?.Invoke($"onmyway received from deviceId={onMyWay.ResponderDeviceId}");
                        OnMyWayReceived?.Invoke(this, onMyWay);
                    }
                    break;

                case AlarmFeedbackMessageType.StatusRelay:
                    var relay = NetworkSerializer.FromJsonLine<AlarmStatusRelayMessage>(envelope.PayloadJson);
                    if (relay is not null && CustomerGroupFilter.Matches(relay.CustomerGroupId, identity.CustomerGroupId))
                    {
                        StatusRelayReceived?.Invoke(this, relay);
                    }
                    break;
            }
        }
        catch (Exception)
        {
            // best-effort - see class remarks
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* handled inside loop */ }
        }
        _cts?.Dispose();
    }
}
