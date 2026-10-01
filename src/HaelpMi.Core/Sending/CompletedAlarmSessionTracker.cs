using System.Collections.Concurrent;

namespace HaelpMi.Core.Sending;

/// <summary>
/// Issue #136: merkt sich Alarm-Sessions, die auf diesem Gerät bereits abgeschlossen sind
/// (Schwellwert erreicht oder Sender hat aufgehört zu pingen), damit eine durch das
/// 5-Sekunden-Repeat ohnehin unvermeidliche, noch unterwegs gewesene "letzte Welle" kein
/// neues Popup mehr erzeugt (siehe <c>AlarmFlowCoordinator.HandleIncomingAlarmRequest</c>) -
/// ohne diese Buchhaltung ist eine bereits geschlossene Session-ID von "nie gesehen" nicht
/// zu unterscheiden. Reine Zeitstempel-Buchhaltung ohne Netzwerk-/UI-Bezug, deshalb ohne
/// WPF-Abhängigkeit eigenständig testbar; der optionale <paramref name="clock"/>-Parameter
/// erlaubt Tests, die Ablauf simulieren, ohne real zu warten.
/// </summary>
public sealed class CompletedAlarmSessionTracker(TimeSpan retention, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _completedAtUtc = new();

    public void MarkCompleted(Guid alarmSessionId)
    {
        var now = _clock();
        _completedAtUtc[alarmSessionId] = now;
        Prune(now);
    }

    public bool IsCompleted(Guid alarmSessionId)
    {
        if (!_completedAtUtc.TryGetValue(alarmSessionId, out var completedAtUtc))
        {
            return false;
        }

        if (_clock() - completedAtUtc > retention)
        {
            // Abgelaufen (keine Spätantworten mehr zu erwarten) - lazily entfernen statt
            // separat zu pollen.
            _completedAtUtc.TryRemove(alarmSessionId, out _);
            return false;
        }

        return true;
    }

    /// <summary>Verhindert unbegrenztes Wachstum über die Laufzeit des Agenten.</summary>
    private void Prune(DateTimeOffset now)
    {
        foreach (var (sessionId, completedAtUtc) in _completedAtUtc)
        {
            if (now - completedAtUtc > retention)
            {
                _completedAtUtc.TryRemove(sessionId, out _);
            }
        }
    }
}
