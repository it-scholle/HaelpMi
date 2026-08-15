using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Security;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Networking;

public sealed class AlarmReceivedEventArgs : EventArgs
{
    public required AlarmRequestMessage Request { get; init; }
    public required IPAddress SenderAddress { get; init; }
}

/// <summary>
/// TCP server side of the alarm channel (5.1/5.2, FR-9/FR-13): accepts a connection,
/// reads one <see cref="AlarmRequestMessage"/>, raises <see cref="AlarmReceived"/> so the
/// caller can show the forced popup, then immediately writes back an
/// <see cref="AlarmAckMessage"/> and closes.
///
/// Important: subscribers of <see cref="AlarmReceived"/> must only *start* showing the
/// popup (e.g. Window.Show(), which returns immediately) and must not block until the
/// user dismisses it - the ack is defined to follow display, not dismissal (FR-13; the
/// dismissal-triggered second ack is now <c>AlarmOnMyWayMessage</c> on the separate
/// feedback channel, Teil 2 Abschnitt 7/8, not sent from here).
/// </summary>
public sealed class AlarmTcpListener : IAsyncDisposable
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Action<string>? _audit;
    private readonly Func<string?>? _groupKeyProvider;
    private readonly DeviceStore _deviceStore = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public event EventHandler<AlarmReceivedEventArgs>? AlarmReceived;

    /// <param name="groupKeyProvider">Siehe AlarmSender-Konstruktor - gleiche Bedeutung, nur für den Empfangs-/Antwortpfad.</param>
    public AlarmTcpListener(Func<LiveIdentity> identityProvider, Action<string>? audit = null, Func<string?>? groupKeyProvider = null)
    {
        _identityProvider = identityProvider;
        _audit = audit;
        _groupKeyProvider = groupKeyProvider;
    }

    public void Start(int port = Models.AppConstants.AlarmTcpPort)
    {
        if (_listener is not null)
        {
            return;
        }

        // Bugfix 06.08.2026 ("Dashboard startet nicht" - Crash-Log-Fund): fehlte hier bisher
        // als einzigem der vier TCP-bindenden Dienste (ConfigSyncService/AlarmFeedbackChannel/
        // EditLockService hatten diesen Fix schon seit 04./05.08.2026). Eine unbehandelte
        // SocketException hier riss die GESAMTE StartBackgroundServices()-Methode in
        // HaelpMi.Agent ab - inklusive allem, was danach kommt (ConfigSync, Update-
        // Verteilung, Hotkeys, IPC-Server, Tray-Icon). Besonders gravierend, weil das
        // GENAU der Alarm-Empfänger ist (FR-9/FR-13) - "degradiert weiterlaufen" bedeutet
        // hier "dieses Gerät empfängt bis zum nächsten erfolgreichen Start keine Alarme
        // mehr", deshalb zusätzlich ins Audit-Log statt nur stillschweigend zu degradieren.
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
        }
        catch (SocketException)
        {
            _listener = null;
            _audit?.Invoke($"AlarmTcpListener konnte Port {port} nicht öffnen (belegt) - Alarme kommen bis zum nächsten Neustart nicht an.");
            return;
        }

        _cts = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(_listener, _cts.Token);
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
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            var senderAddress = ((IPEndPoint?)client.Client.RemoteEndPoint)?.Address ?? IPAddress.None;

            await using var stream = client.GetStream();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            // Untrusted network input (CLAUDE.md): bounded read, not the unbounded
            // StreamReader.ReadLineAsync(), so a sender that never sends '\n' can't make
            // us buffer indefinitely.
            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            var identity = _identityProvider();

            // LAN-Verschlüsselung (CLAUDE.md "Lizenz & Secrets"): eine Zeile ist entweder
            // ein SecureEnvelope (neuer, verschlüsselungsfähiger Absender) oder das alte
            // Klartextformat - beide koexistieren während der Rollout-Übergangsphase, siehe
            // SecureEnvelopeCodec.TryParse-Klassendoku. Die Antwort spiegelt bewusst das
            // Anfrageformat (siehe unten).
            AlarmRequestMessage? request;
            var wasEncrypted = false;

            if (SecureEnvelopeCodec.TryParse(line, out var envelope) && envelope is not null)
            {
                if (!CustomerGroupFilter.Matches(envelope.CustomerGroupId, identity.CustomerGroupId) || !envelope.IsPlausible())
                {
                    return; // different deployment, oder offensichtlich unplausibel - vor jeder teuren Krypto-Operation verwerfen
                }

                var senderPinnedKey = _deviceStore.Load().FirstOrDefault(d => d.DeviceId == envelope.DeviceId)?.PinnedDeviceIdentityPublicKeyBase64;
                var groupKeyBase64 = _groupKeyProvider?.Invoke();
                request = SecureEnvelopeCodec.TryOpen<AlarmRequestMessage>(envelope, groupKeyBase64, senderPinnedKey, DateTimeOffset.UtcNow);
                if (request is null)
                {
                    return; // falscher Gruppenschlüssel, manipuliert, ungepinnter/falscher Absender-Schlüssel o. ä. - stiller Drop wie bei jedem anderen Verifikationsfehlschlag
                }

                wasEncrypted = true;
            }
            else
            {
                try
                {
                    request = NetworkSerializer.FromJsonLine<AlarmRequestMessage>(line);
                }
                catch (Exception)
                {
                    return; // malformed request - no trust assumptions in an admin-less network (NFR-6)
                }
            }

            if (request is null || !request.IsPlausible())
            {
                return;
            }

            if (!CustomerGroupFilter.Matches(request.CustomerGroupId, identity.CustomerGroupId))
            {
                return; // different deployment sharing the same physical network (Teil 2, Abschnitt 6)
            }

            _audit?.Invoke($"alarm received from deviceId={request.SenderDeviceId} isTest={request.IsTest}");
            AlarmReceived?.Invoke(this, new AlarmReceivedEventArgs { Request = request, SenderAddress = senderAddress });

            var ack = new AlarmAckMessage(request.CustomerGroupId, request.AlarmProfileId, request.AlarmSessionId, identity.DeviceId, DateTimeOffset.UtcNow);

            string ackLine;
            if (wasEncrypted)
            {
                var groupKeyBase64 = _groupKeyProvider?.Invoke();
                var devicePrivateKeyBase64 = DeviceIdentityStore.LoadOrCreate().PrivateKeyBase64;
                var ackEnvelope = SecureEnvelopeCodec.Seal(ack, ack.CustomerGroupId, identity.DeviceId, groupKeyBase64, devicePrivateKeyBase64, DateTimeOffset.UtcNow);
                ackLine = ackEnvelope is not null ? NetworkSerializer.ToJsonLine(ackEnvelope) : NetworkSerializer.ToJsonLine(ack);
            }
            else
            {
                ackLine = NetworkSerializer.ToJsonLine(ack);
            }

            var ackBytes = NetworkSerializer.Encoding.GetBytes(ackLine);
            await stream.WriteAsync(ackBytes, ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception)
        {
            // Best-effort delivery: a dropped/slow connection just means the sender
            // counts this target as not-yet-acked (FR-14) - it does not crash the listener.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* already handled inside the loop */ }
        }
        _cts?.Dispose();
    }
}
