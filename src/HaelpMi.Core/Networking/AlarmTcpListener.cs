using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Networking;

public sealed class AlarmReceivedEventArgs : EventArgs
{
    private readonly List<Task<bool>> _displayReports = new();
    private readonly TaskCompletionSource<bool> _firstDisplayed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public required AlarmRequestMessage Request { get; init; }
    public required IPAddress SenderAddress { get; init; }

    /// <summary>Zeitpunkt des Empfangs als <see cref="Stopwatch.GetTimestamp"/>, für Laufzeitmessungen im Empfangspfad.</summary>
    public long ReceivedTimestamp { get; init; } = Stopwatch.GetTimestamp();

    /// <summary>Wird true, sobald eine Rückmeldung „angezeigt“ ist; bleibt sonst offen.</summary>
    internal Task<bool> FirstDisplayedAsync => _firstDisplayed.Task;

    /// <summary>Meldet, ob die Meldung angezeigt wurde. Mehrere Melder (eigene Sitzung, andere Sitzungen) sind erlaubt.</summary>
    public void ReportDisplayed(bool displayed) => ReportDisplayed(Task.FromResult(displayed));

    /// <param name="displayed">Eine Rückmeldung, die erst später feststeht; ein Fehler zählt als „nicht angezeigt“.</param>
    public void ReportDisplayed(Task<bool> displayed)
    {
        lock (_displayReports)
        {
            _displayReports.Add(displayed);
        }

        _ = SignalIfDisplayedAsync(displayed);
    }

    private async Task SignalIfDisplayedAsync(Task<bool> displayed)
    {
        try
        {
            if (await displayed.ConfigureAwait(false))
            {
                _firstDisplayed.TrySetResult(true);
            }
        }
        catch (Exception)
        {
            // Ein fehlgeschlagener Melder zählt als „nicht angezeigt“ (siehe WasDisplayedAsync).
        }
    }

    /// <summary>
    /// true, sobald eine Rückmeldung „angezeigt“ ist; false, wenn alle „nicht angezeigt“ sind. Ohne jede
    /// Rückmeldung true: Wer die Anzeige nicht prüft, bekommt die Quittung wie bisher.
    /// </summary>
    public async Task<bool> WasDisplayedAsync()
    {
        List<Task<bool>> pending;
        lock (_displayReports)
        {
            pending = _displayReports.ToList();
        }

        if (pending.Count == 0)
        {
            return true;
        }

        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending);
            pending.Remove(done);
            if (done.IsCompletedSuccessfully && done.Result)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// TCP server side of the alarm channel (5.1/5.2, FR-9/FR-13): accepts a connection,
/// reads one <see cref="AlarmRequestMessage"/>, raises <see cref="AlarmReceived"/> so the
/// caller can show the forced popup, then writes back an <see cref="AlarmAckMessage"/> and
/// closes - but only if a subscriber reported the popup as displayed
/// (<see cref="AlarmReceivedEventArgs.ReportDisplayed(bool)"/>).
///
/// Ist die Anzeige unterdrückt (Sitzung weggeschaltet, z. B. mitten im Nutzerwechsel) oder kommt
/// die Rückmeldung nicht innerhalb von <see cref="Models.AppConstants.AlarmAckTimeout"/>, bleibt die
/// Quittung aus: Der Sender zählt das Gerät nicht als „empfangen“, die nächste Welle versucht es erneut.
///
/// Die Melder von <see cref="AlarmReceived"/> laufen auf einem eigenen Task, nicht auf der
/// Accept-Schleife. Die Quittung geht raus, sobald ein Melder „angezeigt“ meldet, auch wenn er
/// danach noch arbeitet (z. B. den Ton startet). Sie folgt der Anzeige, nicht dem Schließen
/// (FR-13; die zweite Rückmeldung beim Schließen ist <c>AlarmOnMyWayMessage</c> auf dem
/// Feedback-Kanal, Teil 2 Abschnitt 7/8).
/// </summary>
public sealed class AlarmTcpListener : IAsyncDisposable
{
    private const string LogModul = "Alarmempfang";
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

            // Eigener Task: Kein Handler darf die Annahme der nächsten Verbindung aufhalten.
            _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
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
            var vorgangsId = request.AlarmSessionId.ToString();
            var args = new AlarmReceivedEventArgs { Request = request, SenderAddress = senderAddress };
            BetriebsLog.Debug(LogModul, "Alarm empfangen.", request.SenderDeviceId.ToString(), vorgangsId);

            // Die Melder laufen getrennt: Was sie nach der Anzeige noch tun (z. B. den Ton starten),
            // darf die Quittung nicht verzögern, denn der Sender wartet nur AlarmAckTimeout darauf.
            var melder = Task.Run(() => AlarmReceived?.Invoke(this, args), CancellationToken.None);
            if (!await WasDisplayedInTimeAsync(args, melder, vorgangsId, ct))
            {
                _audit?.Invoke($"alarm nicht quittiert (nicht angezeigt): alarmSessionId={request.AlarmSessionId}");
                return;
            }

            var ack = new AlarmAckMessage(request.CustomerGroupId, request.AlarmProfileId, request.AlarmSessionId, identity.DeviceId, DateTimeOffset.UtcNow);
            var ackLine = NetworkSerializer.ToJsonLine(ack);
            var ackBytes = NetworkSerializer.Encoding.GetBytes(ackLine);
            try
            {
                await stream.WriteAsync(ackBytes, ct);
                await stream.FlushAsync(ct);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // Der Sender zählt das Gerät dann als „nicht empfangen“ und versucht es mit der nächsten Welle erneut.
                BetriebsLog.Warnung(
                    LogModul,
                    $"Quittung nicht zustellbar, {Stopwatch.GetElapsedTime(args.ReceivedTimestamp).TotalMilliseconds:F0} ms nach dem Empfang ({ex.GetType().Name}).",
                    ex,
                    request.SenderDeviceId.ToString(),
                    vorgangsId);
                return;
            }

            BetriebsLog.Debug(LogModul, $"Quittung geschrieben, {Stopwatch.GetElapsedTime(args.ReceivedTimestamp).TotalMilliseconds:F0} ms nach dem Empfang.", request.SenderDeviceId.ToString(), vorgangsId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Listener wird beendet.
        }
        catch (Exception ex)
        {
            // Abgebrochene oder zu langsame Verbindung vor der Quittung: Der Sender zählt das Ziel
            // als „nicht bestätigt“ (FR-14), der Listener läuft weiter.
            BetriebsLog.Debug(LogModul, $"Verbindung ohne Quittung beendet ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>
    /// true, sobald ein Melder „angezeigt“ meldet, auch wenn er danach noch weiterarbeitet; sonst das
    /// Gesamtergebnis, wenn alle Melder fertig sind. Begrenzt die Wartezeit, damit ein hängender Melder
    /// weder Handler noch Socket festhält.
    /// </summary>
    private static async Task<bool> WasDisplayedInTimeAsync(AlarmReceivedEventArgs args, Task melder, string vorgangsId, CancellationToken ct)
    {
        try
        {
            var erstes = await Task.WhenAny(args.FirstDisplayedAsync, AfterMelderAsync(args, melder, vorgangsId))
                .WaitAsync(Models.AppConstants.AlarmAckTimeout, ct);
            return await erstes;
        }
        catch (TimeoutException)
        {
            BetriebsLog.Warnung(LogModul, $"Keine Anzeige-Rückmeldung binnen {Models.AppConstants.AlarmAckTimeout.TotalSeconds:F0} s, keine Quittung.", vorgangsId: vorgangsId);
            return false;
        }
    }

    private static async Task<bool> AfterMelderAsync(AlarmReceivedEventArgs args, Task melder, string vorgangsId)
    {
        try
        {
            await melder;
        }
        catch (Exception ex)
        {
            BetriebsLog.Fehler(LogModul, "Verarbeitung des empfangenen Alarms fehlgeschlagen.", ex, vorgangsId: vorgangsId);
            return false;
        }

        return await args.WasDisplayedAsync();
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
