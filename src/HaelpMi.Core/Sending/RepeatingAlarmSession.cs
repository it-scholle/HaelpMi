using HaelpMi.Core.Diagnostics;
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
/// "noch 2 Minuten sichtbar" (die anderen beiden) zu unterscheiden.
/// </summary>
public enum AlarmStopReason
{
    MaxDuration,
    ThresholdReached,
    Cancelled,
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

    /// <summary>Dieses Geräts eigene Geräte-ID - für TestLogger-Korrelation (SenderStatusWindow kennt _ownIdentity sonst nicht).</summary>
    public Guid OwnDeviceId => _ownIdentity.DeviceId;

    /// <summary>Testmodus-Toggle (Nutzerwunsch 13.08.2026): reicht ins Wire-Format (<see cref="AlarmRequestMessage.IsTest"/>) durch und steuert die Sender-/Empfänger-UI-Kennzeichnung.</summary>
    public bool IsTest { get; }

    private readonly AlarmSender _alarmSender;
    private readonly AlarmFeedbackChannel _feedbackChannel;
    private readonly LiveIdentity _ownIdentity;
    private readonly CancellationTokenSource _stopCts = new();

    // Bugfix 17.08.2026 (Fehlerbericht "Empfangen 0 von N bleibt dauerhaft hängen", Flaw 6):
    // vorher wurde _stopCts.Token direkt als ct an _alarmSender.SendAsync durchgereicht -
    // derselbe Token, den OnMyWayReceived unten beim Erreichen des Schwellwerts sofort
    // cancelt. Eine schnelle "Ich komme"-Antwort (Standard-Schwellwert 1) brach dadurch die
    // noch offene(n) Ack-Wartephase(n) DERSELBEN, gerade laufenden Sendewelle ab, bevor sie
    // echte Acks natürlich einsammeln konnte - ein Ziel zählte dann als "nicht empfangen",
    // obwohl die Zustellung (sonst gäbe es kein "Ich komme") längst stattgefunden hatte.
    // Da danach keine weitere Welle mehr lief, blieb der so verfälschte Zähler dauerhaft
    // stehen. Fix: eigener Token nur für den ECHTEN, manuellen Abbrechen-Pfad (Cancel()) -
    // der Schwellwert-Auto-Stop cancelt weiterhin nur _stopCts (stoppt die nächste Welle/
    // den Delay-Loop), lässt die gerade laufende Ack-Sammlung aber bis zu ihrem echten
    // AlarmAckTimeout auslaufen. Der bestehende Cancel-Test (SendingTests.cs,
    // "...ReturnsQuickly_EvenWhileSendIsStillPendingAgainstAnUnresponsiveTarget") bleibt
    // davon unberührt, weil Cancel() beide Tokens cancelt.
    private readonly CancellationTokenSource _manualCancelCts = new();
    private readonly HashSet<Guid> _onTheWayResponderIds = new();
    private readonly List<string> _onTheWayNames = new();
    private readonly DateTimeOffset _startedAtUtc;
    private int _lastAckedCount;
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
        bool isTest = false)
    {
        Profile = profile;
        Targets = targets;
        _ownIdentity = ownIdentity;
        _alarmSender = alarmSender;
        _feedbackChannel = feedbackChannel;
        _startedAtUtc = startedAtUtc;
        IsTest = isTest;
        _feedbackChannel.OnMyWayReceived += OnMyWayReceived;
    }

    public async Task RunAsync()
    {
        try
        {
            while (!_stopCts.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow - _startedAtUtc >= AppConstants.AlarmMaxDuration)
                {
                    break;
                }

                var result = await _alarmSender.SendAsync(Profile, AlarmSessionId, _ownIdentity, Targets, isTest: IsTest, ct: _manualCancelCts.Token);
                _lastAckedCount = result.AckedCount;
                // Genau die Stelle des v0.35.3-Bugs ("Empfangen-Zaehler bleibt bei schneller
                // Ich-komme-Antwort faelschlich 0") - eine kuenftige Regression zeigt sich hier
                // als AckedCount, das nicht mit den vorangegangenen AckReceived-Zeilen zusammenpasst.
                TestLogger.LogAction(TestLogEventType.StatusChanged, TestLogLevel.Info, TestLogDirection.Local,
                    _ownIdentity.DeviceId, AlarmSessionId, detail: $"AckedCount={result.AckedCount}/{Targets.Count}");
                await RaiseAndRelayAsync(stillSending: true);

                try
                {
                    await Task.Delay(AppConstants.AlarmRepeatInterval, _stopCts.Token);
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
        // Abbrechen sofort verschwinden, bei Schwellwert/Zeitablauf aber noch 2 Minuten
        // sichtbar bleiben - dafür muss sie wissen, WARUM gestoppt wurde, nicht nur DASS.
        // _cancelledByUser wird ausschließlich vom öffentlichen Cancel() gesetzt; ein
        // Schwellwert-Stopp läuft intern direkt über _stopCts.Cancel() in OnMyWayReceived,
        // setzt das Flag also nicht - beide Fälle bleiben damit unterscheidbar.
        StopReason = _cancelledByUser
            ? AlarmStopReason.Cancelled
            : _onTheWayResponderIds.Count >= Profile.ResponseThreshold
                ? AlarmStopReason.ThresholdReached
                : AlarmStopReason.MaxDuration;
        TestLogger.LogAction(TestLogEventType.StatusChanged, TestLogLevel.Info, TestLogDirection.Local,
            _ownIdentity.DeviceId, AlarmSessionId, detail: $"StopReason={StopReason}");

        await RaiseAndRelayAsync(stillSending: false);
        Finished?.Invoke(this, EventArgs.Empty);

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

    /// <summary>Manual "Abbrechen" (FR-50).</summary>
    public void Cancel()
    {
        TestLogger.LogAction(TestLogEventType.StatusChanged, TestLogLevel.Info, TestLogDirection.Local,
            _ownIdentity.DeviceId, AlarmSessionId, detail: "Cancel() aufgerufen");
        _cancelledByUser = true;
        // Beide Tokens: _manualCancelCts bricht eine gerade laufende Ack-Wartephase sofort ab
        // (siehe Feldkommentar) - ein echter Nutzer-Abbruch soll weiterhin sofort greifen,
        // anders als der Schwellwert-Auto-Stop unten in OnMyWayReceived.
        _manualCancelCts.Cancel();
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

    private async Task RaiseAndRelayAsync(bool stillSending)
    {
        var status = new AlarmSessionStatus
        {
            TargetCount = Targets.Count,
            AckedCount = _lastAckedCount,
            OnTheWayNames = _onTheWayNames.ToList(),
            StillSending = stillSending,
        };
        StatusChanged?.Invoke(this, status);

        var relay = new AlarmStatusRelayMessage(
            _ownIdentity.CustomerGroupId, Profile.Id, AlarmSessionId,
            Targets.Count, _lastAckedCount, status.OnTheWayNames, stillSending, DateTimeOffset.UtcNow);
        await _feedbackChannel.RelayStatusAsync(Targets, relay);

        // Nur beim terminalen Relay (stillSending=false) - der aktive Zwischenstand-Relay
        // (stillSending=true) ist kein Stopp-Ereignis. StopReason ist zu diesem Zeitpunkt
        // schon gesetzt (siehe RunAsync, direkt vor dem einzigen stillSending:false-Aufruf;
        // ein später via OnMyWayReceived ausgelöster Nachlauf-Relay sieht denselben, dann
        // schon final gesetzten Wert). Sender-seitig ist StopReason lokal bekannt, deshalb
        // hier bewusst CancelSent statt eines generischen Events, wenn es sich tatsächlich um
        // einen Abbruch handelt (siehe TestLogger-Instrumentierungsplan Flaw 20 für die
        // Begründung, warum der Empfänger das NICHT symmetrisch als CancelReceived loggen kann).
        if (!stillSending)
        {
            var eventType = StopReason == AlarmStopReason.Cancelled ? TestLogEventType.CancelSent : TestLogEventType.MessageSent;
            TestLogger.LogAction(eventType, TestLogLevel.Info, TestLogDirection.Send,
                _ownIdentity.DeviceId, AlarmSessionId, detail: $"StatusRelay StopReason={StopReason}");
        }
    }

    public void Dispose()
    {
        _feedbackChannel.OnMyWayReceived -= OnMyWayReceived;
        _stopCts.Dispose();
        _manualCancelCts.Dispose();
    }
}
