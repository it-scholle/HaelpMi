namespace HaelpMi.Core.Licensing;

/// <summary>
/// Reine Auswertungslogik für die gestaffelte Ablaufwarnung (Issue #20) - keine
/// Abhängigkeit auf ein konkretes Lizenzmodell, nur ein Ablaufdatum. Sobald ein
/// echtes Lizenzobjekt (Issue #19) existiert, reicht dessen Ablaufdatum hier hinein.
/// </summary>
public static class LicenseWarningEvaluator
{
    public static readonly TimeSpan EarlyNoticeThreshold = TimeSpan.FromDays(60);
    public static readonly TimeSpan ReminderThreshold = TimeSpan.FromDays(30);
    public static readonly TimeSpan UrgentThreshold = TimeSpan.FromDays(7);

    public static LicenseWarningStage GetStage(DateTime expiresAtUtc, DateTime nowUtc)
    {
        var remaining = expiresAtUtc - nowUtc;
        if (remaining <= TimeSpan.Zero)
        {
            return LicenseWarningStage.Expired;
        }
        if (remaining <= UrgentThreshold)
        {
            return LicenseWarningStage.Urgent;
        }
        if (remaining <= ReminderThreshold)
        {
            return LicenseWarningStage.Reminder;
        }
        if (remaining <= EarlyNoticeThreshold)
        {
            return LicenseWarningStage.EarlyNotice;
        }
        return LicenseWarningStage.None;
    }

    /// <summary>
    /// Stufe 4 kommt bewusst bei jedem Login erneut (Nutzerfeedback: "kommt eh immer"),
    /// unabhängig von <paramref name="state"/> - siehe <see cref="LicenseWarningReminderState"/>.
    /// </summary>
    public static bool ShouldShowToast(LicenseWarningStage stage, LicenseWarningReminderState? state, DateTime nowUtc)
    {
        if (stage == LicenseWarningStage.None)
        {
            return false;
        }
        if (stage == LicenseWarningStage.Expired)
        {
            return true;
        }
        if (state?.DismissedStage != stage)
        {
            return true;
        }
        return state.SnoozedUntilUtc is { } until && nowUtc >= until;
    }
}
