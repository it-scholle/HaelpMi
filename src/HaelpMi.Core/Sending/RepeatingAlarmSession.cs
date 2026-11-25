using System.Diagnostics;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Sending;

public sealed class AlarmSessionStatus
{
    public required int TargetCount { get; init; }
    public required int AckedCount { get; init; }
    public required IReadOnlyList<string> OnTheWayNames { get; init; }
    public required bool StillSending { get; init; }
}

/// <summary>
/// Why a session stopped pinging (Nutzerwunsch 09.08.2026, siehe <see cref="RepeatingAlarmSession.StopReason"/>):
/// die Sender-Statusanzeige braucht das, um zwischen "sofort weg" (Cancelled) und
/// "noch <see cref="AppConstants.SenderStatusBannerAutoCloseAfterFinish"/> sichtbar" (die
/// anderen beiden) zu unterscheiden.
/// </summary>
public enum AlarmStopReason
{
    MaxDuration,
    ThresholdReached,
    Cancelled,
    /// <summary>Keine Empfänger: genau eine Welle, dann Ende als "nicht zugestellt".</summary>
    NoRecipients,
}

/// <summary>
/// One triggered alarm's full lifecycle (FR-50/FR-53): repeats the send every
/// <see cref="AppConstants.AlarmRepeatInterval"/>, stops after
/// <see cref="AppConstants.AlarmMaxDuration"/>, after
/// <see cref="AlarmProfile.ResponseThreshold"/> distinct "bin unterwegs"
/// responses (the same per-profile Schwellwert the receiver's popup uses to unlock its
/// own "Schließen" button - both sides must agree on the same value), or on manual
/// <see cref="Cancel"/> - whichever comes first. Relays the
/// aggregate status to every recipient after each wave and after each new response, so
/// recipients' popups (FR-51) and this device's own hover popup (FR-53) both stay
/// current without polling.
///
/// The profile/targets passed to the constructor are a snapshot, not a live reference -
/// a Config-Sync hot-reload arriving mid-session (6., known Version-2 challenge: "darf
/// laufende Alarme nicht unterbrechen") cannot change an already-running session's
/// behavior out from under it; it only affects the *next* time this profile's hotkey fires.
/// Fest ist dabei nur die Empfängerliste: Ab der zweiten Welle liest die Session die aktuelle
/// Adresse jedes Ziels per Geräte-ID nach (<c>currentDevicesProvider</c>), weil ein gerade
/// startender Empfänger nach dem Auslösen eine andere Adresse melden kann.
///
/// Pinging (repeated <see cref="AlarmSender.SendAsync"/>) stops the moment any of the
/// three conditions above is met, but the session itself stays alive and keeps listening
/// for <see cref="AlarmOnMyWayMessage"/> for one more <see cref="AppConstants.AlarmAutoCloseAfterLastSignal"/>
/// (same 1-minute window a receiver popup uses before auto-closing itself, FR-52): a
/// late "bin unterwegs" in that window still gets relayed to every recipient so
/// counts/name lists stay correct, just tagged <c>SenderStillSending: false</c> so it
/// reads as a display-only update rather than a new alarm wave.
/// </summary>
public sealed class RepeatingAlarmSession : IDisposable
{
    public Guid AlarmSessionId { get; } = Guid.NewGuid();
    public AlarmProfile Profile { get; }
    public IReadOnlyList<DeviceEntry> Targets { get; }

    private readonly AlarmSender _alarmSender;
    private readonly AlarmFeedbackChannel _feedbackChannel;
    private readonly LiveIdentity _ownIdentity;
    private readonly Func<IReadOnlyList<DeviceEntry>>? _currentDevicesProvider;
    private volatile IReadOnlyList<DeviceEntry> _currentTargets;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly HashSet<Guid> _onTheWayResponderIds = new();
    private readonly List<string> _onTheWayNames = new();
    private readonly DateTimeOffset _startedAtUtc;
    // Über die ganze Session gesammelt statt pro Runde: eine verspätete oder verlorene
    // Quittung einer späteren Runde darf einen schon bestätigten Empfänger nicht wieder
    // auf "nicht empfangen" zurücksetzen. "Bin unterwegs" zählt ebenfalls als Empfang.
    private readonly HashSet<Guid> _receivedDeviceIds = new();
    private bool _pingingActive = true;
    private bool _cancelledByUser;

    public event EventHandler<AlarmSessionStatus>? StatusChanged;

    /// <summary>Fired once pinging has stopped (not once the object is done listening - see class remarks on the 1-minute Nachlauf-Fenster). <see cref="StatusChanged"/> can still fire afterwards for late responses.</summary>
    public event EventHandler? Finished;

    /// <summary>Set right before <see cref="Finished"/> fires - see <see cref="AlarmStopReason"/>.</summary>
    public AlarmStopReason StopReason { get; private set; }

    public RepeatingAlarmSession(
        AlarmProfile profile,
        IReadOnlyList<DeviceEntry> targets,
        LiveIdentity ownIdentity,
        AlarmSender alarmSender,
        AlarmFeedbackChannel feedbackChannel,
        DateTimeOffset startedAtUtc,
        // Liefert die aktuelle Geräteliste (devices.json); darf nicht werfen, sonst endet die Session ohne Finished.
        Func<IReadOnlyList<DeviceEntry>>? currentDevicesProvider = null)
    {
        Profile = profile;
        Targets = targets;
        _currentTargets = targets;
        _currentDevicesProvider = currentDevicesProvider;
        _ownIdentity = ownIdentity;
        _alarmSender = alarmSender;
        _feedbackChannel = feedbackChannel;
        _startedAtUtc = startedAtUtc;
        _feedbackChannel.OnMyWayReceived += OnMyWayReceived;
    }

    public async Task RunAsync()
    {
        var wave = 0;
        try
        {
            while (!_stopCts.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow - _startedAtUtc >= AppConstants.AlarmMaxDuration)
                {
                    break;
                }

                var waveStarted = Stopwatch.StartNew();
                if (wave++ > 0 && _currentDevicesProvider is not null)
                {
                    // Abseits des UI-Threads: das Lesen von devices.json kann bei Dateikonflikten kurz blockieren.
                    _currentTargets = await Task.Run(TargetsWithCurrentAddresses);
                    if (_stopCts.IsCancellationRequested)
                    {
                        break;
                    }
                }

                var result = await _alarmSender.SendAsync(Profile, AlarmSessionId, _ownIdentity, _currentTargets, ct: _stopCts.Token);
                MarkReceived(result.AckedDeviceIds);

                // Nicht abwarten: ein ausgeschaltetes Gerät hält das Relay bis zum Verbindungs-Timeout auf und bremst
                // sonst die nächste Welle für alle. Relays dürfen ohnehin in beliebiger Reihenfolge ankommen (AlarmCloseGate).
                _ = RaiseAndRelayAsync(stillSending: true);

                if (Targets.Count == 0)
                {
                    break;
                }

                try
                {
                    await Task.Delay(TimeUntilNextWave(waveStarted.Elapsed), _stopCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // cancelled mid-send - falls through to the final "stopped" relay below
        }

        // Bugfix 09.08.2026 (Fehlerbericht "Nachzügler-Antworten nach Schwellwert/Stop
        // verschwinden spurlos"): _pingingActive muss VOR dem letzten Relay auf false
        // gehen, weil OnMyWayReceived unten genau dieses Flag als das per Anforderung
        // geforderte "kein Hilferuf mehr, nur Anzeige-Update"-Tag weiterreicht (kein
        // eigenes Protokollfeld nötig - AlarmStatusRelayMessage.SenderStillSending sagt
        // dem Empfänger schon "der Sender pingt nicht mehr", exakt die Bedeutung).
        _pingingActive = false;

        // Nutzerwunsch 09.08.2026: die Sender-Statusanzeige (SenderStatusWindow) soll bei
        // Abbrechen sofort verschwinden, bei Schwellwert/Zeitablauf aber noch
        // AppConstants.SenderStatusBannerAutoCloseAfterFinish sichtbar bleiben - dafür muss
        // sie wissen, WARUM gestoppt wurde, nicht nur DASS.
        // _cancelledByUser wird ausschließlich vom öffentlichen Cancel() gesetzt; ein
        // Schwellwert-Stopp läuft intern direkt über _stopCts.Cancel() in OnMyWayReceived,
        // setzt das Flag also nicht - beide Fälle bleiben damit unterscheidbar.
        StopReason = _cancelledByUser
            ? AlarmStopReason.Cancelled
            : Targets.Count == 0
                ? AlarmStopReason.NoRecipients
                : _onTheWayResponderIds.Count >= Profile.ResponseThreshold
                ? AlarmStopReason.ThresholdReached
                : AlarmStopReason.MaxDuration;

        var finalRelay = await RaiseAndRelayAsync(stillSending: false);
        Finished?.Invoke(this, EventArgs.Empty);

        if (Targets.Count == 0)
        {
            return; // niemand, an den ein Relay ginge oder von dem eine Spätantwort käme
        }

        // Issue #136 Fixvorschlag 5: zusätzliche Exemplare dieses letzten "fertig"-Relays,
        // fire-and-forget - siehe AppConstants.AlarmFinalRelayBurstCount. Bewusst NICHT
        // awaited und nach Finished, nicht davor: ein Abbrechen soll weiterhin sofort beim
        // Sender verschwinden (09.08.2026), die zusätzliche Zustellsicherheit ist reine
        // Netzwerk-Härtung ohne Einfluss auf die lokale Sender-Statusanzeige.
        _ = RelayBurstSender.SendRemainingAsync(
            () => _feedbackChannel.RelayStatusAsync(_currentTargets, finalRelay),
            AppConstants.AlarmFinalRelayBurstCount,
            AppConstants.AlarmFinalRelayBurstSpacing);

        // Weiterhin bis zu AlarmAutoCloseAfterLastSignal (dieselbe 1-Minute-Frist, nach
        // der sich ein Empfänger-Popup ohnehin von selbst schließt) auf OnMyWayReceived
        // hören: bis dahin können Empfänger noch "bin unterwegs" klicken, und diese
        // Antworten sollen trotz gestopptem Pingen noch als Info-Update an alle Geräte
        // gehen (Namensliste/Zähler bleiben korrekt), ohne den Empfänger-Timer neu zu
        // starten (der reagiert nur auf echte Pings, siehe AlarmPopupWindow.
        // NotifyNewSignalReceived - Relays fassen ihn nicht an). Vorher wurde hier sofort
        // durchgereicht zu Dispose() (siehe RunSessionAsync), das die Subscription kappte -
        // jede Spätantwort verschwand dadurch komplett, sender- wie empfängerseitig.
        await Task.Delay(AppConstants.AlarmAutoCloseAfterLastSignal);
    }

    /// <summary>
    /// Der Takt zählt ab dem Start einer Welle, nicht ab ihrem Ende: Warten auf Quittungen und Relays verlängert
    /// sonst jede Runde, und ein langsamer Empfänger bremst die Wiederholung für alle Empfänger.
    /// </summary>
    internal static TimeSpan TimeUntilNextWave(TimeSpan waveDuration)
    {
        var remaining = AppConstants.AlarmRepeatInterval - waveDuration;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>Manual "Abbrechen" (FR-50).</summary>
    public void Cancel()
    {
        if (!_pingingActive)
        {
            return; // schon beendet; ohne Empfänger ist die Sitzung dann womöglich bereits entsorgt
        }

        _cancelledByUser = true;
        _stopCts.Cancel();
    }

    private void OnMyWayReceived(object? sender, AlarmOnMyWayMessage message)
    {
        if (message.AlarmProfileId != Profile.Id || message.AlarmSessionId != AlarmSessionId)
        {
            return;
        }

        if (!_onTheWayResponderIds.Add(message.ResponderDeviceId))
        {
            return; // already counted this device for this session
        }

        _onTheWayNames.Add($"{message.ResponderRoomName} - {message.ResponderComputerName} ({message.ResponderUser})");
        MarkReceived(new[] { message.ResponderDeviceId });

        // Fire-and-forget: RelayStatusAsync/SendEnvelopeAsync already swallow per-target
        // failures internally, so there is nothing here that can throw unobserved.
        //
        // stillSending: _pingingActive statt fest true (09.08.2026) - eine Antwort, die erst
        // nach dem Stop reinkommt (Schwellwert schon erreicht/Zeit abgelaufen/Abbrechen
        // geklickt, siehe Nachlauf-Fenster in RunAsync), ist per Anforderung "kein Hilferuf
        // mehr, nur Anzeige-Update" - genau das sagt SenderStillSending=false dem Empfänger
        // schon aus. Während der aktiven Phase bleibt es weiterhin true wie bisher.
        _ = RaiseAndRelayAsync(stillSending: _pingingActive);

        // Bugfix 07.08.2026 (Fehlerbericht "Popup poppt nach Schließen wieder auf, obwohl
        // schon reagiert wurde"): hier stand bisher der feste AppConstants.
        // AlarmAutoStopResponseCount (=2, ein Phase-1-Rest) statt des pro Profil im Dashboard
        // einstellbaren Profile.ResponseThreshold ("Schwellwert (Antworten bis schließbar)").
        // Mit Schwellwert=1 (Standard bei neuen Profilen) reichte die eine "bin unterwegs"-
        // Antwort dem Empfänger-Popup zum Freischalten von "Schließen" (das benutzt
        // ResponseThreshold schon richtig, siehe AlarmFlowCoordinator/AlarmPopupWindow) -
        // dem SENDER aber nicht zum Stoppen der Wiederholung, weil er weiter auf 2 wartete.
        // Der Sender schickte alle 5s eine neue Anfrage, und ein Empfänger ohne offenes
        // Fenster für diese Session (weil gerade erst geschlossen) bekam prompt ein neues
        // Popup - sah aus wie "poppt nach dem Schließen wieder auf", war eigentlich "der
        // Sender hatte nie wirklich aufgehört zu senden".
        if (_onTheWayResponderIds.Count >= Profile.ResponseThreshold)
        {
            _stopCts.Cancel(); // FR-50: auto-stop once enough people are on their way
        }
    }

    private void MarkReceived(IEnumerable<Guid> deviceIds)
    {
        lock (_receivedDeviceIds)
        {
            foreach (var deviceId in deviceIds)
            {
                if (Targets.Any(t => t.DeviceId == deviceId))
                {
                    _receivedDeviceIds.Add(deviceId);
                }
            }
        }
    }

    private int ReceivedCount()
    {
        lock (_receivedDeviceIds)
        {
            return _receivedDeviceIds.Count;
        }
    }

    private async Task<AlarmStatusRelayMessage> RaiseAndRelayAsync(bool stillSending)
    {
        var ackedCount = ReceivedCount();
        var status = new AlarmSessionStatus
        {
            TargetCount = Targets.Count,
            AckedCount = ackedCount,
            OnTheWayNames = _onTheWayNames.ToList(),
            StillSending = stillSending,
        };
        StatusChanged?.Invoke(this, status);

        var relay = new AlarmStatusRelayMessage(
            _ownIdentity.CustomerGroupId, Profile.Id, AlarmSessionId,
            Targets.Count, ackedCount, status.OnTheWayNames, stillSending, DateTimeOffset.UtcNow);
        await _feedbackChannel.RelayStatusAsync(_currentTargets, relay);
        return relay;
    }

    private IReadOnlyList<DeviceEntry> TargetsWithCurrentAddresses()
    {
        if (_currentDevicesProvider is null)
        {
            return Targets;
        }

        var currentById = new Dictionary<Guid, DeviceEntry>();
        foreach (var device in _currentDevicesProvider())
        {
            currentById.TryAdd(device.DeviceId, device);
        }

        return Targets
            .Select(target => currentById.TryGetValue(target.DeviceId, out var current)
                && !string.IsNullOrEmpty(current.IpAddress)
                && (current.IpAddress != target.IpAddress || current.TcpPort != target.TcpPort)
                    ? target.WithAddress(current.IpAddress, current.TcpPort)
                    : target)
            .ToList();
    }

    public void Dispose()
    {
        _feedbackChannel.OnMyWayReceived -= OnMyWayReceived;
        _stopCts.Dispose();
    }
}
