namespace HaelpMi.Core.Ipc;

/// <summary>
/// Local same-machine actions Config asks the always-running Agent to perform,
/// because the Agent owns the actual network sockets (5.2). Ping just checks
/// reachability, e.g. before enabling buttons that need it.
/// </summary>
public enum IpcCommandType
{
    Ping,
    Rebroadcast,
    SearchAgain,
    SelfTest,

    /// <summary>Testmodus-Toggle im Konfigurator scharfschalten (Nutzerwunsch 13.08.2026, One-Shot mit Timeout).</summary>
    ArmTestMode,

    /// <summary>Manuelles Wieder-Ausschalten - reiner UX-Komfort, siehe TestModeArmState-Klassendoku.</summary>
    DisarmTestMode,

    /// <summary>Aktuellen Testmodus-Countdown abfragen (Re-Sync beim Öffnen/Fokussieren des Konfigurators).</summary>
    TestModeStatus,
}

public sealed record IpcRequest(IpcCommandType Command);

public sealed record IpcResponse(bool Success, string? Error = null, TimeSpan? Remaining = null);
