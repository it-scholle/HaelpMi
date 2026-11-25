namespace HaelpMi.Core.Sending;

/// <summary>
/// Entscheidet, ob ein Empfänger-Alarmfenster geschlossen werden darf (FR-47/FR-51). Frei wird es,
/// sobald genug Personen "Bin unterwegs" gemeldet haben oder der Sender endgültig aufgehört hat zu
/// pingen (Abbrechen, Schwellwert erreicht, Zeitablauf). Die Freigabe bleibt danach bestehen: Status-Relays
/// laufen über parallele Verbindungen ohne Reihenfolge, ein verspätetes mit kleinerem Zähler darf ein
/// schon freigegebenes Fenster nicht wieder sperren.
/// </summary>
public sealed class AlarmCloseGate(int responseThreshold)
{
    /// <summary>Mindestens 1, auch wenn das Profil 0 oder weniger enthält.</summary>
    public int ResponseThreshold { get; } = Math.Max(1, responseThreshold);

    /// <summary>Zähler aus dem zuletzt eingetroffenen Status-Relay, nur für die Anzeige.</summary>
    public int OnTheWayCount { get; private set; }

    public bool IsCloseAllowed { get; private set; }

    public int MissingResponses => Math.Max(0, ResponseThreshold - OnTheWayCount);

    public void ApplyStatusRelay(int onTheWayCount, bool senderStillSending)
    {
        OnTheWayCount = onTheWayCount;
        IsCloseAllowed |= onTheWayCount >= ResponseThreshold || !senderStillSending;
    }
}
