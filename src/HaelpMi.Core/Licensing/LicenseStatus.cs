namespace HaelpMi.Core.Licensing;

/// <summary>
/// Ergebnis der Lizenzprüfung. Ablauf ist Soft-Expiry (CLAUDE.md "Lizenz &amp; Secrets":
/// "kein Hard-Lock") - <see cref="Expired"/> blockiert das Programm nicht, sondern ist nur
/// die Grundlage für die Ablaufwarnung aus Issue #20.
/// </summary>
public enum LicenseStatus
{
    /// <summary>Keine Lizenzdatei gefunden.</summary>
    Missing,

    /// <summary>
    /// Datei vorhanden, aber nicht lesbar, mit ungültiger Signatur oder für eine andere
    /// Kundengruppe ausgestellt - defekt oder manipuliert wird hier nicht unterschieden.
    /// </summary>
    Invalid,

    /// <summary>Signatur und Kundengruppe passen, Ablaufdatum liegt in der Vergangenheit.</summary>
    Expired,

    /// <summary>Signatur und Kundengruppe passen, Ablaufdatum liegt in der Zukunft.</summary>
    Valid,
}
