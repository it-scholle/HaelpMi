namespace HaelpMi.Core.Licensing;

/// <summary>Ob der Lizenz-Hinweis (Toast) jetzt erscheint - beim Agent-Start und bei jeder Prüfung im laufenden Betrieb.</summary>
public static class LicenseReminderTrigger
{
    /// <param name="previous">Zuletzt ausgewertete Stufe; <c>null</c> beim Agent-Start.</param>
    /// <param name="isUserDevice">
    /// Die Sitzung darf das Dashboard nicht öffnen. Dann nur bei abgelaufener Lizenz: Fehlt sie oder ist sie
    /// ungültig, ist das Gerät gesperrt und zeigt den Sperr-Hinweis; "läuft bald ab" ist Sache des Admins.
    /// </param>
    /// <param name="snoozed">"Später erinnern" ist aktiv; gilt nur beim Start, eine neue Stufe im Betrieb zeigt der Hinweis trotzdem.</param>
    public static bool ShouldShow(LicenseWarningLevel? previous, LicenseWarningLevel current, bool isUserDevice, bool snoozed)
    {
        var relevant = isUserDevice ? current == LicenseWarningLevel.Expired : current != LicenseWarningLevel.None;
        if (!relevant)
        {
            return false;
        }

        return previous is null ? !snoozed : previous != current;
    }
}
