namespace HaelpMi.Core.Licensing;

/// <summary>
/// Wann der Agent den Lizenzstand ohne Neustart erneut prüft: zur Warnschwelle, kurz nach dem
/// Ablauf und sonst spätestens nach <see cref="MaxInterval"/> (fängt Uhrumstellungen und neue
/// Tageszählung ab). Rein lokal, kein Netzwerkverkehr.
/// </summary>
public static class LicenseRecheckSchedule
{
    public static readonly TimeSpan MaxInterval = TimeSpan.FromDays(1);

    /// <summary><see cref="LicenseReader"/> wertet erst "Ablauf &lt; jetzt" als abgelaufen, deshalb knapp danach prüfen.</summary>
    private static readonly TimeSpan AfterExpiryMargin = TimeSpan.FromSeconds(1);

    public static DateTime NextCheckUtc(LicenseCheckResult result, DateTime utcNow)
    {
        var next = utcNow + MaxInterval;
        if (result.Status != LicenseStatus.Valid || result.License is not { } license)
        {
            return next;
        }

        var warningAt = license.ExpiryDateUtc.AddDays(-LicenseWarningEvaluator.WarningWindowDays);
        var expiredAt = license.ExpiryDateUtc + AfterExpiryMargin;
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
