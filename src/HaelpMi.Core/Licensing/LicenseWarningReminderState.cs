namespace HaelpMi.Core.Licensing;

/// <summary>
/// Persistierter Aufschub-Zustand für den Ablauf-Toast (Issue #20). Deckt sowohl
/// "Ignorieren" (<see cref="SnoozedUntilUtc"/> bleibt null, unterdrückt bis zur
/// nächsten Stufe) als auch "Erinnere mich in X" (<see cref="SnoozedUntilUtc"/>
/// gesetzt, unterdrückt nur bis zu diesem Zeitpunkt, sofern die Stufe bis dahin
/// gleich bleibt) ab - siehe <see cref="LicenseWarningEvaluator.ShouldShowToast"/>.
/// Für <see cref="LicenseWarningStage.Expired"/> irrelevant, dort gibt es keinen Aufschub.
/// </summary>
public sealed class LicenseWarningReminderState
{
    public LicenseWarningStage? DismissedStage { get; set; }

    public DateTime? SnoozedUntilUtc { get; set; }
}
