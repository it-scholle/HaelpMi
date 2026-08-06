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
/// </summary>
public sealed class AlarmFeedbackChannel : IAsyncDisposable
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Action<string>? _audit;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public event EventHandler<AlarmOnMyWayMessage>? OnMyWayReceived;
    public event EventHandler<AlarmStatusRelayMessage>? StatusRelayReceived;

    public AlarmFeedbackChannel(Func<LiveIdentity> identityProvider, Action<string>? audit = null)
    {
        _identityProvider = identityProvider;
        _audit = audit;
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

            var identity = _identityProvider();

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
