using HaelpMi.Core.Sending;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Issue #136 ("Alarmfenster lässt sich trotz erreichter Schwelle nicht schließen"):
/// deckt den eigentlichen Kernfix ab - ohne diese Buchhaltung ist eine Session-ID nach dem
/// Schließen des Empfänger-Popups ununterscheidbar von "nie gesehen", und eine durch das
/// 5-Sekunden-Repeat ohnehin unvermeidliche, bereits unterwegs gewesene "letzte Welle"
/// erzeugt ein komplett neues Popup mit zurückgesetztem Zustand (siehe AlarmFlowCoordinator).
/// </summary>
public class CompletedAlarmSessionTrackerTests
{
    [Fact]
    public void IsCompleted_ReturnsFalse_ForUnknownSession()
    {
        var tracker = new CompletedAlarmSessionTracker(TimeSpan.FromMinutes(1));

        Assert.False(tracker.IsCompleted(Guid.NewGuid()));
    }

    [Fact]
    public void IsCompleted_ReturnsTrue_RightAfterMarking()
    {
        var tracker = new CompletedAlarmSessionTracker(TimeSpan.FromMinutes(1));
        var sessionId = Guid.NewGuid();

        tracker.MarkCompleted(sessionId);

        Assert.True(tracker.IsCompleted(sessionId));
    }

    [Fact]
    public void IsCompleted_ReturnsFalse_AfterRetentionWindowExpired()
    {
        // Regressionstest für genau das in Issue #136 gefundene Race: eine stray letzte
        // Alarm-Welle, die erst NACH dem Schließen eintrifft, darf nur innerhalb der
        // Nachlauf-Frist (dieselbe wie RepeatingAlarmSession.AlarmAutoCloseAfterLastSignal)
        // unterdrückt werden - danach ist die Session wirklich vorbei und ein neuer Alarm
        // mit zufällig derselben Guid (praktisch ausgeschlossen, aber die Zeitgrenze muss
        // trotzdem greifen) soll wieder normal ein Popup zeigen können.
        var now = DateTimeOffset.UtcNow;
        var tracker = new CompletedAlarmSessionTracker(TimeSpan.FromSeconds(10), () => now);
        var sessionId = Guid.NewGuid();

        tracker.MarkCompleted(sessionId);
        Assert.True(tracker.IsCompleted(sessionId));

        now = now.AddSeconds(11);

        Assert.False(tracker.IsCompleted(sessionId));
    }

    [Fact]
    public void IsCompleted_DoesNotExpireTooEarly_WithinRetentionWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var tracker = new CompletedAlarmSessionTracker(TimeSpan.FromSeconds(10), () => now);
        var sessionId = Guid.NewGuid();

        tracker.MarkCompleted(sessionId);
        now = now.AddSeconds(9);

        Assert.True(tracker.IsCompleted(sessionId));
    }

    [Fact]
    public void MarkCompleted_PrunesOtherExpiredEntries_WithoutAffectingStillValidOnes()
    {
        var now = DateTimeOffset.UtcNow;
        var tracker = new CompletedAlarmSessionTracker(TimeSpan.FromSeconds(10), () => now);
        var oldSession = Guid.NewGuid();
        var freshSession = Guid.NewGuid();

        tracker.MarkCompleted(oldSession);
        now = now.AddSeconds(11); // oldSession ist jetzt abgelaufen
        tracker.MarkCompleted(freshSession); // löst intern eine Bereinigung aus

        Assert.False(tracker.IsCompleted(oldSession));
        Assert.True(tracker.IsCompleted(freshSession));
    }

    [Fact]
    public void MarkCompleted_IsIdempotent_ForTheSameSessionCalledTwice()
    {
        var tracker = new CompletedAlarmSessionTracker(TimeSpan.FromMinutes(1));
        var sessionId = Guid.NewGuid();

        tracker.MarkCompleted(sessionId);
        tracker.MarkCompleted(sessionId);

        Assert.True(tracker.IsCompleted(sessionId));
    }
}
