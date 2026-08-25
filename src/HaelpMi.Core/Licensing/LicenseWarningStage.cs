namespace HaelpMi.Core.Licensing;

/// <summary>
/// Gestaffelte Ablaufwarnung (Issue #20). <c>None</c> heißt: weder Toast noch
/// Statuszeile zeigen etwas - Ablaufdatum liegt weiter als <see cref="LicenseWarningEvaluator.EarlyNoticeThreshold"/> entfernt.
/// </summary>
public enum LicenseWarningStage
{
    None,
    EarlyNotice,
    Reminder,
    Urgent,
    Expired,
}
