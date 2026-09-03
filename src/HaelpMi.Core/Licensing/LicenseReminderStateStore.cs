using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Issue #20-Nacharbeit: "Später erinnern"-Zustand für das Systemstart-Popup
/// (<c>LicenseReminderToastWindow</c>, <c>HaelpMi.Agent</c>). Ein Klick auf "Später
/// erinnern" ist das EINZIGE, was das Popup unterdrückt - jeder andere Weg, es zu
/// schließen (X, oder es einfach ignorieren), zeigt es beim nächsten Agent-Start wieder
/// (Nutzervorgabe 02.09.2026).
/// </summary>
public static class LicenseReminderStateStore
{
    /// <summary>Wie lange ein Klick auf "Später erinnern" das Popup unterdrückt.</summary>
    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromHours(24);

    public static void Snooze() =>
        JsonFileStore.Save(AppPaths.LicenseReminderStateFilePath, new LicenseReminderState { SnoozedUntilUtc = DateTime.UtcNow + SnoozeDuration });

    public static bool IsSnoozed(DateTime utcNow)
    {
        var state = JsonFileStore.Load<LicenseReminderState>(AppPaths.LicenseReminderStateFilePath);
        return state?.SnoozedUntilUtc is { } snoozedUntil && snoozedUntil > utcNow;
    }
}
