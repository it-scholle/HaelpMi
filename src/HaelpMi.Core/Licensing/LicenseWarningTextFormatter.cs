namespace HaelpMi.Core.Licensing;

/// <summary>
/// Einheitliches Textformat für Toast und Statuszeile (Issue #20, Nutzerfeedback
/// "vorn Datum (X Tage)"): immer Datum zuerst, Tageszahl in Klammern dahinter.
/// </summary>
public static class LicenseWarningTextFormatter
{
    public static string TitleFor(LicenseWarningStage stage) => stage switch
    {
        LicenseWarningStage.Urgent => "Lizenz läuft in Kürze ab",
        LicenseWarningStage.Expired => "Lizenz abgelaufen",
        _ => "Lizenz läuft bald ab",
    };

    public static string SubtextFor(LicenseWarningStage stage, DateTime expiresAtUtc, DateTime nowUtc)
    {
        var date = expiresAtUtc.ToString("dd.MM.yyyy");
        if (stage == LicenseWarningStage.Expired)
        {
            var daysAgo = Math.Max(0, (int)Math.Floor((nowUtc - expiresAtUtc).TotalDays));
            return $"seit {date} (vor {daysAgo} {DayWordDativ(daysAgo)})";
        }

        var daysLeft = Math.Max(0, (int)Math.Ceiling((expiresAtUtc - nowUtc).TotalDays));
        return $"{date} (noch {daysLeft} {DayWordAkkusativ(daysLeft)})";
    }

    // "noch 1 Tag" / "noch 24 Tage" (Akkusativ) vs. "vor 1 Tag" / "vor 12 Tagen" (Dativ) -
    // im Singular gleich, im Plural unterscheidet sich nur die Dativ-Form durch das -n.
    private static string DayWordAkkusativ(int days) => days == 1 ? "Tag" : "Tage";

    private static string DayWordDativ(int days) => days == 1 ? "Tag" : "Tagen";
}
