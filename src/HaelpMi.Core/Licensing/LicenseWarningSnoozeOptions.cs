namespace HaelpMi.Core.Licensing;

/// <summary>
/// Auswahlbare "Erinnere mich in"-Zeitspannen je Stufe (Issue #20) - je Stufe bewusst
/// so gedeckelt, dass keine Option über die jeweils nächste Stufe hinausreicht.
/// Stufe 4 hat keine Optionen (kein Aufschub, siehe <see cref="LicenseWarningEvaluator.ShouldShowToast"/>).
/// </summary>
public static class LicenseWarningSnoozeOptions
{
    public static IReadOnlyList<(TimeSpan Duration, string Label)> For(LicenseWarningStage stage) => stage switch
    {
        LicenseWarningStage.EarlyNotice =>
        [
            (TimeSpan.FromDays(7), "1 Woche"),
            (TimeSpan.FromDays(14), "2 Wochen"),
            (TimeSpan.FromDays(28), "4 Wochen"),
        ],
        LicenseWarningStage.Reminder =>
        [
            (TimeSpan.FromDays(3), "3 Tage"),
            (TimeSpan.FromDays(7), "1 Woche"),
            (TimeSpan.FromDays(14), "2 Wochen"),
        ],
        LicenseWarningStage.Urgent =>
        [
            (TimeSpan.FromDays(1), "1 Tag"),
            (TimeSpan.FromDays(2), "2 Tage"),
            (TimeSpan.FromDays(3), "3 Tage"),
        ],
        _ => [],
    };
}
