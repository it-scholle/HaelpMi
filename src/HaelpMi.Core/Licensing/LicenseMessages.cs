namespace HaelpMi.Core.Licensing;

/// <summary>
/// Baut die für einen Admin anzuzeigenden Warntexte aus einem <see cref="LicenseCheckResult"/>.
/// Bewusst UI-Framework-frei (nur Strings), damit sowohl der reine Text-Tray-Balloon im
/// Agent als auch das Toast-Fenster in Config denselben Wortlaut zeigen. Jeder Text nennt
/// explizit, dass die Alarmfunktion uneingeschränkt weiterläuft - macht die
/// Nicht-Verhandelbarkeit für den Admin sichtbar, nicht nur im Code wahr.
/// </summary>
public static class LicenseMessages
{
    private const string AlarmUnaffectedNotice = "Die Alarmfunktion (Auslösen und Empfangen) läuft uneingeschränkt weiter.";

    public static (string Title, string Body, bool Severe) BuildAdminNotice(LicenseCheckResult result)
    {
        if (result.Standing == LicenseStanding.Good)
        {
            return ("HälpMi - Lizenz in Ordnung", "Keine Warnung fällig.", false);
        }

        if (result.StageIndex == LicenseEvaluator.MissingOrInvalidStageIndex)
        {
            return (
                "HälpMi - Keine gültige Lizenz gefunden",
                $"Es liegt keine gültige Lizenzdatei vor (fehlend, beschädigt oder falsch signiert). {AlarmUnaffectedNotice} " +
                "Bitte eine gültige Lizenzdatei einspielen.",
                true);
        }

        var customerText = string.IsNullOrWhiteSpace(result.CustomerName) ? string.Empty : $" ({result.CustomerName})";

        if (result.Standing == LicenseStanding.NotGood)
        {
            var daysOverdue = -(result.DaysUntilExpiry ?? 0);
            return (
                "HälpMi - Lizenz abgelaufen",
                $"Die Lizenz{customerText} ist seit {daysOverdue} Tag(en) abgelaufen (Ablaufdatum: {result.ExpiresOnUtc:dd.MM.yyyy}). " +
                $"{AlarmUnaffectedNotice} Bitte die Lizenz verlängern.",
                true);
        }

        var daysLeft = result.DaysUntilExpiry ?? 0;
        return (
            "HälpMi - Lizenz läuft bald ab",
            $"Die Lizenz{customerText} läuft in {daysLeft} Tag(en) ab (Ablaufdatum: {result.ExpiresOnUtc:dd.MM.yyyy}). " +
            $"{AlarmUnaffectedNotice} Bitte rechtzeitig verlängern.",
            false);
    }
}
