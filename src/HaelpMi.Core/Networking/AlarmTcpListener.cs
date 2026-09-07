using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

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
    private readonly Func<bool>? _isOwnDeviceLicenseDisabled;
    private readonly Action<string>? _audit;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public event EventHandler<AlarmReceivedEventArgs>? AlarmReceived;

    /// <param name="isOwnDeviceLicenseDisabled">
    /// Issue #59/#60: liefert true, solange dieses Gerät lizenzüberschritten ist - eine
    /// eingehende Verbindung wird dann sofort ohne Antwort geschlossen (nicht gelesen, kein
    /// Ack), damit das Gerät für andere komplett unerreichbar wirkt statt nur die Anzeige zu
    /// unterdrücken. Null (Standard) verhält sich wie "nie deaktiviert" - für Tests, die
    /// diesen Aspekt nicht prüfen.
    /// </param>
    public AlarmTcpListener(Func<LiveIdentity> identityProvider, Action<string>? audit = null, Func<bool>? isOwnDeviceLicenseDisabled = null)
    {
        _identityProvider = identityProvider;
        _audit = audit;
        _isOwnDeviceLicenseDisabled = isOwnDeviceLicenseDisabled;
    }

    /// <returns>
    /// true, wenn der Port erfolgreich gebunden wurde. false = eine andere Instanz auf
    /// dieser Maschine hält den Port bereits (Issue #9: bei Fast User Switching normal für
    /// jede Sitzung außer der zuerst gestarteten Primary - siehe <see cref="AlarmChannel"/>,
    /// die genau dieses Ergebnis für die Primary/Satellite-Entscheidung braucht) oder ein
    /// anderer Bind-Fehler.
    /// </returns>
    public bool Start(int port = Models.AppConstants.AlarmTcpPort)
    {
        if (_listener is not null)
        {
            return true;
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
            // Seit Issue #9 (Fast User Switching) der Normalfall für jede Sitzung außer der
            // zuerst gestarteten Primary, nicht mehr zwingend ein Fehler - AlarmChannel
            // fängt dieses false ab und verbindet stattdessen als Satellite über den
            // lokalen Relay-Kanal.
            _listener = null;
            _audit?.Invoke($"AlarmTcpListener konnte Port {port} nicht öffnen (belegt) - vermutlich bereits Primary in einer anderen Sitzung.");
            return false;
        }

        _cts = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(_listener, _cts.Token);
        return true;
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
            if (_isOwnDeviceLicenseDisabled?.Invoke() == true)
            {
                // Issue #60 ("nicht erreichbar sein"): Verbindung wird ohne jede Reaktion
                // geschlossen - kein Lesen, kein Ack. Aus Sicht des Senders identisch zu
                // einem nicht erreichbaren Gerät (AlarmSender.SendToOneAsync wertet das
                // als "nicht bestätigt"), keine Sonderbehandlung dort nötig.
                return;
            }

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

            AlarmRequestMessage? request;
            try
            {
                request = NetworkSerializer.FromJsonLine<AlarmRequestMessage>(line);
            }
            catch (Exception)
            {
                return; // malformed request - no trust assumptions in an admin-less network (NFR-6)
            }

            if (request is null || !request.IsPlausible())
            {
                return;
            }

            var identity = _identityProvider();
            if (!CustomerGroupFilter.Matches(request.CustomerGroupId, identity.CustomerGroupId))
            {
                return; // different deployment sharing the same physical network (Teil 2, Abschnitt 6)
            }

            _audit?.Invoke($"alarm received from deviceId={request.SenderDeviceId}");
            AlarmReceived?.Invoke(this, new AlarmReceivedEventArgs { Request = request, SenderAddress = senderAddress });

            var ack = new AlarmAckMessage(request.CustomerGroupId, request.AlarmProfileId, request.AlarmSessionId, identity.DeviceId, DateTimeOffset.UtcNow);
            var ackLine = NetworkSerializer.ToJsonLine(ack);
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
