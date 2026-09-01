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
    /// Prüfung, siehe #19/LicenseReader) - damit ein per <see cref="ImportLicenseFile"/>
    /// gerade erst eingespielter Lizenzstand sofort sichtbar wird, ohne das Dashboard neu
    /// zu starten.
    /// </summary>
    public required Func<LicenseCheckResult> GetLicenseStatus { get; init; }

    /// <summary>"Lizenz einspielen"-Button im #20-Banner (Issue #51) - nimmt den vom Windows-Datei-Picker gewählten Pfad entgegen, siehe <see cref="LicenseImporter"/>.</summary>
    public required Func<string, LicenseImportResult> ImportLicenseFile { get; init; }
}

public sealed record UserInstallerExportResult(bool Success, string? OutputFilePath, string? Error);
