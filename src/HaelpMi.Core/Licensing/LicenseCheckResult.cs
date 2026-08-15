namespace HaelpMi.Core.Licensing;

/// <summary>
/// Fehlende/kaputte Datei, ungültige Signatur und abgelaufen werden bewusst zu einem
/// einzigen "nicht in Ordnung"-Zustand zusammengefasst - die Aufgabenstellung behandelt
/// alle drei ohnehin gleich: nie blockieren, immer den Admin warnen. Eine feinere
/// Unterscheidung wäre hier reine Diagnose-Information ohne aktuellen Anwendungsfall.
/// </summary>
public enum LicenseStanding
{
    Good,
    Warning,
    NotGood,
}

/// <summary>
/// Ergebnis von <see cref="LicenseEvaluator.Evaluate"/>. StageIndex ist die eine Zahl, die
/// <see cref="LicenseChecker"/> für die Eskalations-/Drosselungslogik braucht ("ist der
/// Zustand schlimmer geworden als beim letzten Mal gezeigten?").
/// </summary>
public sealed record LicenseCheckResult(
    LicenseStanding Standing,
    string? CustomerName,
    DateOnly? ExpiresOnUtc,
    int? DaysUntilExpiry,
    int StageIndex);
