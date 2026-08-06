namespace HaelpMi.Core.Ipc;

/// <summary>
/// Was der (rechtelose) Agent dem privilegierten HaelpMi.UpdateService über den lokalen
/// Named Pipe (CLAUDE.md: "niemals TCP über das Netzwerk") anweisen kann (Abschnitt 11).
/// Jeder Schritt einzeln, nicht ein "UpdateTo(version)"-Rundumschlag - der Agent
/// entscheidet zwischen den Schritten (eigener Selbsttest, Peer-Bestätigung abwarten),
/// der Dienst selbst kennt kein größeres Update-Ablauf-Konzept, nur diese vier Aktionen.
/// </summary>
public enum UpdateServiceCommandType
{
    /// <summary>Entpackt ein bereits lokal abgelegtes, signaturgeprüftes Paket nach {app}\versions\{version}\.</summary>
    Install,

    /// <summary>Startet die installierte Version testweise auf einem separaten Port, parallel zur laufenden Produktivversion.</summary>
    StartTest,

    /// <summary>Übernimmt die getestete Version als neue Produktivversion (Port-Übernahme + Deinstallation der Altversion) - der eigentliche 0-Downtime-Swap.</summary>
    ConfirmSwap,

    /// <summary>Verwirft eine installierte/getestete, aber nicht bestätigte Version - die Produktivversion bleibt unangetastet.</summary>
    Rollback,
}

public sealed record UpdateServiceRequest(
    UpdateServiceCommandType Command,
    string Version,
    string? PackageZipPath = null,
    int? TestPort = null);

public sealed record UpdateServiceResponse(bool Success, string? Error = null);
