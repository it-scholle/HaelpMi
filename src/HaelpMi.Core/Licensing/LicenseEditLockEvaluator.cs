namespace HaelpMi.Core.Licensing;

/// <summary>
/// Issue #77: ohne gültige Lizenz bleibt die laufende Konfiguration unverändert aktiv (CLAUDE.md
/// "Soft-Expiry, kein Hard-Lock" gilt für den Betrieb) - die BEARBEITUNG im Admin-Dashboard
/// (Gruppen/Alarm-Profile anlegen, ändern, löschen) wird aber gesperrt, bis wieder eine gültige
/// Lizenz vorliegt. Eigene, kleine testbare Klasse statt Inline-Logik im Dashboard-Code-Behind -
/// gleiches Prinzip wie <see cref="LicenseWarningEvaluator"/>.
/// </summary>
public static class LicenseEditLockEvaluator
{
    /// <summary><see cref="LicenseWarningLevel.ExpiringSoon"/> sperrt NICHT - die Lizenz ist bis zum Ablauf noch gültig.</summary>
    public static bool IsEditingLocked(LicenseWarningLevel level) =>
        level is LicenseWarningLevel.Missing or LicenseWarningLevel.Invalid or LicenseWarningLevel.Expired;
}
