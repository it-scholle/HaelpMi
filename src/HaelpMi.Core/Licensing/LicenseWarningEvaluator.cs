namespace HaelpMi.Core.Licensing;

/// <summary>
/// Issue #20 (Info-Popup/Banner vor Lizenzablauf): staffelt die reine #19-Prüfung um eine
/// Vorwarnstufe, die schon VOR dem eigentlichen Ablauf greift, damit der Admin nicht erst
/// beim Soft-Expiry selbst überrascht wird.
/// </summary>
public static class LicenseWarningEvaluator
{
    /// <summary>Ab wie vielen Tagen vor Ablauf eine noch gültige Lizenz bereits als "läuft bald ab" gilt.</summary>
    public const int WarningWindowDays = 30;

    public static LicenseWarning Evaluate(LicenseCheckResult result, DateTime utcNow)
    {
        switch (result.Status)
        {
            case LicenseStatus.Missing:
                return new LicenseWarning(LicenseWarningLevel.Missing, null);
            case LicenseStatus.Invalid:
                return new LicenseWarning(LicenseWarningLevel.Invalid, null);
            case LicenseStatus.Expired:
                return new LicenseWarning(LicenseWarningLevel.Expired, DaysRemaining(result.License!, utcNow));
            default:
                var daysRemaining = DaysRemaining(result.License!, utcNow);
                var level = daysRemaining <= WarningWindowDays ? LicenseWarningLevel.ExpiringSoon : LicenseWarningLevel.None;
                return new LicenseWarning(level, daysRemaining);
        }
    }

    private static int DaysRemaining(License license, DateTime utcNow) =>
        (int)Math.Floor((license.ExpiryDateUtc - utcNow).TotalDays);
}
