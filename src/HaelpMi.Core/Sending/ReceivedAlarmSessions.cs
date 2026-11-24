using System.Collections.Concurrent;

namespace HaelpMi.Core.Sending;

public enum IncomingAlarmAction
{
    ShowNewPopup,
    RefreshOpenPopup,
    IgnoreCompletedSession,
    SuppressInactiveSession,
}

/// <summary>
/// Empfängerseitiger Zustand je Alarm-Session: offenes Alarmfenster oder bereits abgeschlossen. Daraus folgt,
/// was eine eingehende Welle bewirkt. Der Sender wiederholt jede Session alle paar Sekunden; eine Welle, die
/// schon unterwegs war, als die Session endete, darf kein neues Fenster öffnen.
/// <see cref="Decide"/>, <see cref="PopupShown"/> und <see cref="PopupClosed"/> müssen auf demselben Thread
/// laufen (UI-Dispatcher). Sonst kann sich ein Fenster zwischen Entscheidung und Anzeige schließen, und die
/// Welle öffnet ein zweites Fenster für eine schon erledigte Session.
/// </summary>
/// <typeparam name="TPopup">Das Alarmfenster; Tests verwenden einen Platzhalter.</typeparam>
public sealed class ReceivedAlarmSessions<TPopup>(TimeSpan completedRetention, Func<DateTimeOffset>? clock = null)
    where TPopup : class
{
    private readonly ConcurrentDictionary<Guid, TPopup> _openPopups = new();
    private readonly CompletedAlarmSessionTracker _completed = new(completedRetention, clock);

    /// <param name="sessionVisible">false, wenn die eigene Windows-Sitzung gerade weggeschaltet ist; ein neues Fenster sähe dort niemand.</param>
    /// <param name="openPopup">Bei <see cref="IncomingAlarmAction.RefreshOpenPopup"/> das offene Fenster, sonst null.</param>
    public IncomingAlarmAction Decide(Guid alarmSessionId, bool sessionVisible, out TPopup? openPopup)
    {
        openPopup = null;
        if (_completed.IsCompleted(alarmSessionId))
        {
            return IncomingAlarmAction.IgnoreCompletedSession;
        }

        if (_openPopups.TryGetValue(alarmSessionId, out openPopup))
        {
            return IncomingAlarmAction.RefreshOpenPopup;
        }

        return sessionVisible ? IncomingAlarmAction.ShowNewPopup : IncomingAlarmAction.SuppressInactiveSession;
    }

    public void PopupShown(Guid alarmSessionId, TPopup popup) => _openPopups[alarmSessionId] = popup;

    /// <param name="closeAllowed">
    /// Stand von <see cref="AlarmCloseGate.IsCloseAllowed"/> beim Schließen. false heißt Auto-Close ohne
    /// ausreichende Rückmeldung (FR-52); eine weitere Welle öffnet dann wieder ein Fenster (FR-51).
    /// </param>
    public void PopupClosed(Guid alarmSessionId, bool closeAllowed)
    {
        _openPopups.TryRemove(alarmSessionId, out _);
        if (closeAllowed)
        {
            _completed.MarkCompleted(alarmSessionId);
        }
    }

    /// <summary>Ein Status-Relay mit <paramref name="senderStillSending"/>=false beendet die Session, auch ohne offenes Fenster.</summary>
    /// <returns>Das offene Fenster dieser Session, das das Relay anzeigen soll, sonst null.</returns>
    public TPopup? StatusRelayReceived(Guid alarmSessionId, bool senderStillSending)
    {
        if (!senderStillSending)
        {
            _completed.MarkCompleted(alarmSessionId);
        }

        return _openPopups.TryGetValue(alarmSessionId, out var popup) ? popup : null;
    }
}
