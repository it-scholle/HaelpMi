using HaelpMi.Core.Sending;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Schließpfad auf dem Empfänger ohne WPF: Wellen, Status-Relays und Schließen einer Alarm-Session in der
/// Reihenfolge, in der AlarmFlowCoordinator und AlarmPopupWindow sie verarbeiten. <see cref="TestPopup"/>
/// steht für das Alarmfenster und trägt dessen <see cref="AlarmCloseGate"/>.
/// </summary>
public class ReceivedAlarmSessionsTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(1);

    private sealed class TestPopup(int responseThreshold)
    {
        public AlarmCloseGate Gate { get; } = new(responseThreshold);
    }

    private static ReceivedAlarmSessions<TestPopup> NewSessions(Func<DateTimeOffset>? clock = null) => new(Retention, clock);

    /// <summary>Erste Welle wie im Coordinator: entscheiden, bei ShowNewPopup ein Fenster anlegen und eintragen.</summary>
    private static TestPopup ShowFirstWave(ReceivedAlarmSessions<TestPopup> sessions, Guid sessionId, int threshold)
    {
        Assert.Equal(IncomingAlarmAction.ShowNewPopup, sessions.Decide(sessionId, sessionVisible: true, out var existing));
        Assert.Null(existing);
        var popup = new TestPopup(threshold);
        sessions.PopupShown(sessionId, popup);
        return popup;
    }

    /// <summary>Status-Relay wie in HandleStatusRelay: Session-Buchhaltung, dann das offene Fenster aktualisieren.</summary>
    private static void ApplyRelay(ReceivedAlarmSessions<TestPopup> sessions, Guid sessionId, int onTheWayCount, bool senderStillSending) =>
        sessions.StatusRelayReceived(sessionId, senderStillSending)?.Gate.ApplyStatusRelay(onTheWayCount, senderStillSending);

    [Fact]
    public void RepeatWave_WhileOpen_RefreshesTheSamePopup()
    {
        var sessions = NewSessions();
        var sessionId = Guid.NewGuid();
        var popup = ShowFirstWave(sessions, sessionId, 1);

        Assert.Equal(IncomingAlarmAction.RefreshOpenPopup, sessions.Decide(sessionId, sessionVisible: true, out var existing));
        Assert.Same(popup, existing);
    }

    [Fact]
    public void ThresholdReached_CloseStaysAllowed_WhenARepeatWaveOfTheSameSessionArrivesAfterwards()
    {
        var sessions = NewSessions();
        var sessionId = Guid.NewGuid();
        var popup = ShowFirstWave(sessions, sessionId, 1);

        ApplyRelay(sessions, sessionId, 1, senderStillSending: true);
        Assert.True(popup.Gate.IsCloseAllowed);

        // Welle, die der Sender vor dem Erreichen der Schwelle losgeschickt hatte.
        Assert.Equal(IncomingAlarmAction.RefreshOpenPopup, sessions.Decide(sessionId, sessionVisible: true, out var existing));
        Assert.Same(popup, existing);
        Assert.True(popup.Gate.IsCloseAllowed);

        // Ein verspätetes Relay aus derselben Welle mit dem alten Zählerstand.
        ApplyRelay(sessions, sessionId, 0, senderStillSending: true);
        Assert.True(popup.Gate.IsCloseAllowed);
    }

    [Fact]
    public void LateWave_AfterRegularClose_OpensNoNewPopup()
    {
        var sessions = NewSessions();
        var sessionId = Guid.NewGuid();
        var popup = ShowFirstWave(sessions, sessionId, 1);
        ApplyRelay(sessions, sessionId, 1, senderStillSending: true);

        sessions.PopupClosed(sessionId, popup.Gate.IsCloseAllowed);

        Assert.Equal(IncomingAlarmAction.IgnoreCompletedSession, sessions.Decide(sessionId, sessionVisible: true, out var existing));
        Assert.Null(existing);
    }

    [Fact]
    public void LateWave_AfterCloseAndFinalRelay_OpensNoNewPopup()
    {
        var sessions = NewSessions();
        var sessionId = Guid.NewGuid();
        var popup = ShowFirstWave(sessions, sessionId, 1);
        ApplyRelay(sessions, sessionId, 1, senderStillSending: true);
        sessions.PopupClosed(sessionId, popup.Gate.IsCloseAllowed);

        // Letztes Relay des Senders erst nach dem Schließen; es findet kein Fenster mehr.
        Assert.Null(sessions.StatusRelayReceived(sessionId, senderStillSending: false));

        Assert.Equal(IncomingAlarmAction.IgnoreCompletedSession, sessions.Decide(sessionId, sessionVisible: true, out _));
    }

    [Fact]
    public void AutoCloseWithoutEnoughResponses_NextWaveReopensThePopup()
    {
        // FR-51: vorzeitig geschlossen (hier: FR-52-Auto-Close), solange der Sender noch alarmiert.
        var sessions = NewSessions();
        var sessionId = Guid.NewGuid();
        var popup = ShowFirstWave(sessions, sessionId, 1);

        sessions.PopupClosed(sessionId, popup.Gate.IsCloseAllowed);

        Assert.Equal(IncomingAlarmAction.ShowNewPopup, sessions.Decide(sessionId, sessionVisible: true, out _));
    }

    [Fact]
    public void SenderCancels_OpenPopupBecomesClosable_AndNoLaterWaveReopensIt()
    {
        var sessions = NewSessions();
        var sessionId = Guid.NewGuid();
        var popup = ShowFirstWave(sessions, sessionId, 2);

        ApplyRelay(sessions, sessionId, 0, senderStillSending: false);
        Assert.True(popup.Gate.IsCloseAllowed);

        // Noch offen: eine verspätete Welle aktualisiert nichts mehr, die Session ist beendet.
        Assert.Equal(IncomingAlarmAction.IgnoreCompletedSession, sessions.Decide(sessionId, sessionVisible: true, out _));

        sessions.PopupClosed(sessionId, popup.Gate.IsCloseAllowed);
        Assert.Equal(IncomingAlarmAction.IgnoreCompletedSession, sessions.Decide(sessionId, sessionVisible: true, out _));
    }

    [Fact]
    public void SenderCancels_BeforeTheFirstWaveArrives_NoPopupIsShown()
    {
        var sessions = NewSessions();
        var sessionId = Guid.NewGuid();

        Assert.Null(sessions.StatusRelayReceived(sessionId, senderStillSending: false));

        Assert.Equal(IncomingAlarmAction.IgnoreCompletedSession, sessions.Decide(sessionId, sessionVisible: true, out _));
    }

    [Fact]
    public void ThresholdTwo_TwoRecipients_BothPopupsOpenOnlyAfterTheSecondResponse()
    {
        // Zwei Empfänger, je eigene Buchhaltung; der Sender schickt jedes Relay an beide.
        var sessionId = Guid.NewGuid();
        var sessionsB = NewSessions();
        var sessionsC = NewSessions();
        var popupB = ShowFirstWave(sessionsB, sessionId, 2);
        var popupC = ShowFirstWave(sessionsC, sessionId, 2);

        void RelayToBoth(int count, bool stillSending)
        {
            ApplyRelay(sessionsB, sessionId, count, stillSending);
            ApplyRelay(sessionsC, sessionId, count, stillSending);
        }

        RelayToBoth(1, stillSending: true);
        Assert.False(popupB.Gate.IsCloseAllowed);
        Assert.False(popupC.Gate.IsCloseAllowed);

        // Repeat-Welle zwischen den beiden Rückmeldungen.
        Assert.Equal(IncomingAlarmAction.RefreshOpenPopup, sessionsB.Decide(sessionId, sessionVisible: true, out _));
        Assert.False(popupB.Gate.IsCloseAllowed);

        RelayToBoth(2, stillSending: true);
        Assert.True(popupB.Gate.IsCloseAllowed);
        Assert.True(popupC.Gate.IsCloseAllowed);

        sessionsB.PopupClosed(sessionId, popupB.Gate.IsCloseAllowed);
        sessionsC.PopupClosed(sessionId, popupC.Gate.IsCloseAllowed);
        Assert.Equal(IncomingAlarmAction.IgnoreCompletedSession, sessionsB.Decide(sessionId, sessionVisible: true, out _));
        Assert.Equal(IncomingAlarmAction.IgnoreCompletedSession, sessionsC.Decide(sessionId, sessionVisible: true, out _));
    }

    [Fact]
    public void InactiveWindowsSession_SuppressesANewPopup_ButStillRefreshesAnOpenOne()
    {
        var sessions = NewSessions();
        var newSession = Guid.NewGuid();
        Assert.Equal(IncomingAlarmAction.SuppressInactiveSession, sessions.Decide(newSession, sessionVisible: false, out _));

        var openSession = Guid.NewGuid();
        var popup = ShowFirstWave(sessions, openSession, 1);
        Assert.Equal(IncomingAlarmAction.RefreshOpenPopup, sessions.Decide(openSession, sessionVisible: false, out var existing));
        Assert.Same(popup, existing);
    }

    [Fact]
    public void CompletedSession_IsForgottenAfterTheRetentionWindow()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var sessions = NewSessions(() => now);
        var sessionId = Guid.NewGuid();
        var popup = ShowFirstWave(sessions, sessionId, 1);
        ApplyRelay(sessions, sessionId, 1, senderStillSending: true);
        sessions.PopupClosed(sessionId, popup.Gate.IsCloseAllowed);

        now += Retention + TimeSpan.FromSeconds(1);

        Assert.Equal(IncomingAlarmAction.ShowNewPopup, sessions.Decide(sessionId, sessionVisible: true, out _));
    }
}
