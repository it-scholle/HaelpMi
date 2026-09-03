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

    /// <summary>
    /// Issue #20-Nacharbeit (Nutzerbericht 03.09.2026): Config schickt dies, sobald ein
    /// Lizenzschlüssel erfolgreich eingespielt wurde (<c>LicenseImportOutcome.Activated</c>),
    /// damit der Agent ein noch offenes Systemstart-Erinnerungs-Popup (siehe
    /// <c>LicenseReminderToastWindow</c>) selbst schließt, statt veraltet stehen zu bleiben.
    /// </summary>
    LicenseRenewed,
}

public sealed record IpcRequest(IpcCommandType Command);

public sealed record IpcResponse(bool Success, string? Error = null);
