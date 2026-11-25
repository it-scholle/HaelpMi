namespace HaelpMi.Core.Licensing;

/// <summary>
/// Wann der Agent den Lizenzstand ohne Neustart erneut prüft: zur Warnschwelle, kurz nach dem
/// Ablauf und sonst spätestens nach <see cref="MaxInterval"/> (fängt Uhrumstellungen und neue
/// Tageszählung ab). Rein lokal, kein Netzwerkverkehr.
/// </summary>
public static class LicenseRecheckSchedule
{
    public static readonly TimeSpan MaxInterval = TimeSpan.FromDays(1);

    /// <summary>Beide Schwellen gelten erst echt danach (<see cref="LicenseReader"/>: "Ablauf &lt; jetzt", Evaluator: abgerundete Resttage), deshalb knapp danach prüfen.</summary>
    private static readonly TimeSpan AfterThresholdMargin = TimeSpan.FromSeconds(1);

    public static DateTime NextCheckUtc(LicenseCheckResult result, DateTime utcNow)
    {
        var next = utcNow + MaxInterval;
        if (result.Status != LicenseStatus.Valid || result.License is not { } license)
        {
            return next;
        }

        // LicenseWarningEvaluator rundet die Resttage ab: "bald ab" gilt schon ab weniger als WarningWindowDays + 1 Tagen.
        var warningAt = license.ExpiryDateUtc.AddDays(-(LicenseWarningEvaluator.WarningWindowDays + 1)) + AfterThresholdMargin;
        var expiredAt = license.ExpiryDateUtc + AfterThresholdMargin;
        foreach (var candidate in new[] { warningAt, expiredAt })
        {
            if (candidate > utcNow && candidate < next)
            {
                next = candidate;
            }
        }

        return next;
    }
}
