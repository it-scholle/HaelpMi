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
    int? TestPort = null,
    // Flaw 26 (v0.39.x): nur bei ConfirmSwap gesetzt - die eigene Prozess-ID des
    // aufrufenden Produktiv-Agents. Ersetzt das vorherige "alle HaelpMi.Agent nach Namen
    // killen" (das u.a. den eigenen wartenden Aufrufer treffen konnte, ohne dass irgendwer
    // je geprüft hätte, ob der Kill überhaupt durchschlug) durch ein gezieltes Beenden mit
    // abgewartetem Prozessende. Null = Aufrufer noch auf einem Stand vor diesem Fix (oder
    // ein anderer Aufrufer als der Agent selbst) - UpdateServiceWorker fällt dann auf das
    // alte, namensbasierte Verhalten zurück statt den Request abzulehnen.
    int? CallerProcessId = null);

public sealed record UpdateServiceResponse(bool Success, string? Error = null);
