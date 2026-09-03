namespace HaelpMi.Core.Licensing;

/// <summary>
/// Issue #20-Nacharbeit: "Später erinnern" im Systemstart-Popup. Nullable Zeitstempel statt
/// eines Bool-Flags (gleiches Prinzip wie an anderen Wartezustands-Stellen im Projekt) - ein
/// abgelaufener Snooze braucht keinen expliziten Reset-Schritt, er verliert seine Wirkung
/// einfach von selbst, sobald <c>DateTime.UtcNow</c> ihn überholt hat.
/// </summary>
public sealed class LicenseReminderState
{
    public DateTime? SnoozedUntilUtc { get; set; }
}
