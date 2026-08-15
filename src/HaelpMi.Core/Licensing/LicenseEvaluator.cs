namespace HaelpMi.Core.Licensing;

/// <summary>
/// Reine Auswertung "wie dringend ist der Lizenzzustand" - keine I/O, kein Zustand, nur
/// Zeitstempel-Arithmetik über den übergebenen Tag (gleiches Idiom wie
/// <see cref="Models.TestModeArmState"/>: testbar ohne ein echtes Datum abzuwarten).
///
/// Eskalationsleiter (Nutzerwunsch: "sich zuspitzende Warnungen", Vorlauf 30 Tage
/// vorgeschlagen): Stufe 0 = gut, Stufen 1-4 = 30/14/7/1 Tage vor Ablauf (Stufe 4 = am
/// dringendsten), Stufen 5-8 = 0/7/30/90 Tage überfällig, Stufe 100 = keine verifizierbare
/// Lizenz überhaupt (mehr Unsicherheit als bei einer bloß überfälligen Lizenz, deshalb die
/// höchste Stufe).
/// </summary>
public static class LicenseEvaluator
{
    // Aufsteigend sortiert (kleinster Schwellwert zuerst geprüft) - der KLEINSTE noch
    // zutreffende Schwellwert gewinnt, damit wenige verbleibende Tage die höhere
    // (dringlichere) Stufe ergeben, nicht die niedrigste.
    private static readonly int[] PreExpiryWarningDaysAscending = { 1, 7, 14, 30 };

    // Aufsteigend sortiert, hier gewinnt bewusst der GRÖSSTE noch zutreffende Schwellwert
    // (siehe Schleife unten) - mehr überfällige Tage sollen die höhere Stufe ergeben.
    private static readonly int[] PostExpiryEscalationDaysAscending = { 0, 7, 30, 90 };

    /// <summary>Höchste Stufe überhaupt - fehlende/ungültige Lizenz ist dringlicher als jede Ablauf-Stufe.</summary>
    public const int MissingOrInvalidStageIndex = 100;

    public static LicenseCheckResult Evaluate(LicenseFile? license, DateOnly todayUtc)
    {
        if (license is null)
        {
            return new LicenseCheckResult(LicenseStanding.NotGood, null, null, null, MissingOrInvalidStageIndex);
        }

        var daysUntilExpiry = license.ExpiresOnUtc.DayNumber - todayUtc.DayNumber;

        if (daysUntilExpiry < 0)
        {
            var daysOverdue = -daysUntilExpiry;
            var stage = PreExpiryWarningDaysAscending.Length; // Basisstufe "gerade erst abgelaufen"
            for (var i = 0; i < PostExpiryEscalationDaysAscending.Length; i++)
            {
                if (daysOverdue >= PostExpiryEscalationDaysAscending[i])
                {
                    stage = PreExpiryWarningDaysAscending.Length + i + 1;
                }
            }

            return new LicenseCheckResult(LicenseStanding.NotGood, license.CustomerName, license.ExpiresOnUtc, daysUntilExpiry, stage);
        }

        for (var i = 0; i < PreExpiryWarningDaysAscending.Length; i++)
        {
            if (daysUntilExpiry <= PreExpiryWarningDaysAscending[i])
            {
                var stageIndex = PreExpiryWarningDaysAscending.Length - i;
                return new LicenseCheckResult(LicenseStanding.Warning, license.CustomerName, license.ExpiresOnUtc, daysUntilExpiry, stageIndex);
            }
        }

        return new LicenseCheckResult(LicenseStanding.Good, license.CustomerName, license.ExpiresOnUtc, daysUntilExpiry, 0);
    }
}
