using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;

namespace HaelpMi.UI.ViewModels;

/// <summary>
/// Everything AdminDashboardWindow needs from the outside world, injected by the hosting
/// app (HaelpMi.Config) so this UI project stays free of network/transport specifics -
/// same pattern as <see cref="ConfigWindowContext"/>.
/// </summary>
public sealed class AdminDashboardContext
{
    public required Func<SharedConfig> LoadConfig { get; init; }
    public required Func<List<DeviceEntry>> LoadDevices { get; init; }

    /// <summary>
    /// Nutzer-Frage 04.08.2026 ("wie kommt es, dass der Admin sich selbst nicht als Nutzer
    /// erkennt"): <see cref="LoadDevices"/> enthält nur über Boot-Call ENTDECKTE Peers - ein
    /// Gerät trägt sich nie selbst dort ein (kein Sinn in Selbst-Discovery per Netzwerk).
    /// Für FR-35 ("Admin-Rechner dient zugleich als Testgerät") muss der Admin sich aber
    /// selbst als Sender/Empfänger/Gruppenmitglied auswählen können, auch wenn noch kein
    /// einziges anderes Gerät im Netz gefunden wurde - dafür synthetisiert dieser Delegate
    /// aus der lokalen Identität einen passenden DeviceEntry.
    /// </summary>
    public required Func<DeviceEntry> LoadOwnDevice { get; init; }

    /// <summary>Mutates a working copy, saves, records history, and broadcasts the new version (Teil 2, Abschnitt 10). Params mirror ConfigSyncService.PublishAsync.</summary>
    public required Func<Func<SharedConfig, SharedConfig>, EditScopeKind, Guid, string, string?, string?, Task<SharedConfig>> Publish { get; init; }

    /// <summary>Reverts the most recent change recorded for the given Gruppe/Alarm-Profil.</summary>
    public required Func<EditScopeKind, Guid, Task<bool>> Undo { get; init; }

    /// <summary>
    /// Exklusiver Edit-Lock (CLAUDE.md, Abschnitt 5): wird aufgerufen, sobald der Admin
    /// eine Gruppe oder ein Alarm-Profil zum Bearbeiten auswählt - nicht mehr einmalig
    /// beim Öffnen des Dashboards (siehe EditScope.cs für den Hintergrund der Änderung).
    /// </summary>
    public required Func<EditScopeKind, Guid, Task<EditLockAcquireResult>> AcquireLock { get; init; }

    /// <summary>Gibt einen zuvor über <see cref="AcquireLock"/> erworbenen Lock wieder frei (Auswahl gewechselt oder Dashboard geschlossen).</summary>
    public required Action<EditScopeKind, Guid> ReleaseLock { get; init; }

    /// <summary>
    /// "User-Installer exportieren" (Nutzerwunsch): baut aus dem im Admin-Installer
    /// mitgelieferten Bausatz (siehe HaelpMiCommon.iss.inc) einen frischen, zur eigenen
    /// Kunden-Gruppen-ID passenden User-Installer und legt ihn im Downloads-Ordner ab -
    /// beliebig oft wiederholbar, kein erneuter Install-Creator-Lauf nötig.
    /// </summary>
    public required Func<Task<UserInstallerExportResult>> ExportUserInstaller { get; init; }

    /// <summary>
    /// "Updates"-Tab (Nutzerwunsch 09.08.2026): Versionen, die dieses Gerät bereits lokal
    /// im P2P-Cache hat (per Auto-Seed aus dem eigenen Installer-Bausatz oder per früherem
    /// P2P-Pull) und die der Admin daher für einen Rollout freigeben könnte - siehe
    /// UpdatePackageCacheStore.ListAvailableVersions.
    /// </summary>
    public required Func<List<string>> ListAvailableUpdateVersions { get; init; }

    /// <summary>
    /// Issue #20: live neu ausgewertet statt gecacht (billiger Dateizugriff + Ed25519-
    /// Prüfung, siehe #19/LicenseReader) - damit ein per <see cref="ImportLicenseKeyText"/>
    /// gerade erst eingespielter Lizenzstand sofort sichtbar wird, ohne das Dashboard neu
    /// zu starten.
    /// </summary>
    public required Func<LicenseCheckResult> GetLicenseStatus { get; init; }

    /// <summary>
    /// "Lizenz einspielen"-Button im #20-Banner (Issue #51), Text statt Datei-Auswahl seit
    /// Issue #54-Nacharbeit (01.09.2026) - siehe <see cref="LicenseImporter.ImportFromKeyText(string, Guid)"/>.
    /// Der ursprüngliche Datei-Import (<see cref="LicenseImporter.Import(string, Guid)"/>)
    /// bleibt als eigenständige, getestete Fähigkeit bestehen, hängt nur an keinem
    /// UI-Button mehr.
    /// </summary>
    public required Func<string, LicenseImportDiagnosis> ImportLicenseKeyText { get; init; }

    /// <summary>
    /// Issue #20-Nacharbeit (Nutzerbericht 03.09.2026): nach erfolgreichem "Lizenz
    /// einspielen" muss der Agent (separater Prozess) ein noch offenes Systemstart-
    /// Erinnerungs-Popup selbst schließen können - Fire-and-Forget per IPC
    /// (IpcCommandType.LicenseRenewed), scheitert best-effort wie
    /// NotifyLocalAgentOfConfigChange, falls der Agent gerade nicht erreichbar ist.
    /// </summary>
    public required Action NotifyLicenseRenewed { get; init; }

    /// <summary>
    /// Issue #60: DeviceIds, die laut <see cref="LicenseLimitGuard"/> gerade das
    /// Lizenzkontingent überschreiten - Grundlage für das Dashboard-Banner "nicht
    /// lizenziertes Gerät".
    /// </summary>
    public required Func<IReadOnlySet<Guid>> GetDisabledDeviceIds { get; init; }

    /// <summary>Issue #61 (Geräte-Tab "x/y lizenziert"): das "y" - null bei unbegrenzter Lizenz (XL).</summary>
    public required Func<int?> GetLicenseSeatLimit { get; init; }

    /// <summary>"Gelesen"-Klick im Lizenzlimit-Banner (Issue #60) - siehe DeviceEntry.LicenseLimitWarningAcknowledged.</summary>
    public required Action<Guid> AcknowledgeLicenseLimitWarning { get; init; }

    /// <summary>
    /// Toggle im Geräte-Tab (Issue #61): setzt <see cref="DeviceEntry.LicenseOverride"/>
    /// lokal (mit aktuellem Zeitstempel, siehe DeviceStore.SetLicenseOverride) und stößt
    /// danach denselben "Erneut suchen"-Announce an, den auch der manuelle Knopf in
    /// ConfigWindow auslöst (IpcCommandType.SearchAgain) - die neue Entscheidung erreicht
    /// Peers dadurch sofort per Gossip statt erst beim nächsten passiven Boot-Call-Kontakt.
    /// </summary>
    public required Action<Guid, LicenseOverride> SetDeviceLicenseOverride { get; init; }

    /// <summary>"Löschen" im Geräte-Tab (Issue #61) - siehe DeviceStore.Remove für die (bewusst rein lokale) Semantik.</summary>
    public required Action<Guid> DeleteDevice { get; init; }

    /// <summary>Notiz-Feld im Geräte-Tab (Issue #61) - rein lokal wie bisher, kein Gossip (siehe DeviceEntry.Note).</summary>
    public required Action<Guid, string> SetDeviceNote { get; init; }

    /// <summary>"Erneut suchen" im Geräte-Tab (Issue #61) - identischer IPC-Weg wie ConfigWindowContext.RequestSearchAgain.</summary>
    public required Func<Task<bool>> RequestSearchAgain { get; init; }
}

public sealed record UserInstallerExportResult(bool Success, string? OutputFilePath, string? Error);
