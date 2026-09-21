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

    /// <summary>
    /// Issue #113: Config schickt dies, nachdem ein Admin sich im Geräte-Tab selbst auf
    /// aktiv/deaktiviert gesetzt und dabei direkt <c>OwnSettings.LicenseOverride</c>
    /// geschrieben hat - der Agent lädt die Settings darauf neu (sonst erst beim nächsten
    /// Programmstart wirksam) und meldet die Entscheidung per
    /// <c>DiscoveryService.AnnounceSelfLicenseOverrideAsync</c> sofort ans Netz weiter.
    /// </summary>
    OwnLicenseOverrideChanged,
}

public sealed record IpcRequest(IpcCommandType Command);

public sealed record IpcResponse(bool Success, string? Error = null);
