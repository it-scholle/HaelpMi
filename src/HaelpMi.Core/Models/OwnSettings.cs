namespace HaelpMi.Core.Models;

/// <summary>
/// Purely local, per-device, non-synced state: persistent identity, the cached-from-
/// <see cref="DeploymentInfo"/> role/customer id, and the cached-from-
/// <see cref="SharedConfig"/> room/circle assignment for *this* device.
///
/// Historical note (CLAUDE.md v2): in Phase 1 this held an editable "Allgemein" tab
/// (name/user/room/hotkey/alarm text, all typed in locally) plus the single alarm
/// message. That per-device self-configuration concept is gone - Pflichtenheft Teil 2,
/// Abschnitt 2: "User-Installer fragt nichts davon ab ... das übernimmt ausschließlich
/// der Admin über das Dashboard." What remains here is either auto-derived (computer
/// name, current Windows user) or a local cache of what the Admin's
/// <see cref="SharedConfig"/> says about this specific device - never something typed
/// into a "Allgemein"-style form on a User-role device again.
/// </summary>
public sealed class OwnSettings
{
    /// <summary>Generated once on first run, never regenerated on update/reinstall (Phase 1 FR-3, 6.).</summary>
    public required Guid DeviceId { get; set; }

    /// <summary>Windows computer name, read once via Environment.MachineName - not user-editable (Teil 2, Abschnitt 2).</summary>
    public string ComputerName { get; set; } = string.Empty;

    /// <summary>
    /// Local cache of this device's own <see cref="DeviceAssignment"/> from the last
    /// applied <see cref="SharedConfig"/> - kept here too so the Konfigurationsanzeige
    /// and the device's own announce/boot-call have something to show even before the
    /// very first Config-Sync has been received (falls back to values entered in the
    /// Admin Ersteinrichtung wizard for an Admin-role device's own first run).
    /// </summary>
    public string RoomName { get; set; } = string.Empty;

    public string RoomNumber { get; set; } = string.Empty;

    public string IncomingSoundId { get; set; } = IncomingSoundCatalog.DefaultId;

    /// <summary>Cached from DeploymentInfo at first run - see <see cref="Models.Role"/> for why this isn't user-togglable.</summary>
    public Role Role { get; set; } = Role.User;

    public Guid CustomerGroupId { get; set; }

    /// <summary>The highest <see cref="SharedConfig.ConfigVersion"/> this device has applied so far.</summary>
    public int AppliedConfigVersion { get; set; }

    /// <summary>False only until the Ersteinrichtung wizard has been completed once (Admin-role) or the first Config-Sync has been applied (User-role).</summary>
    public bool FirstRunCompleted { get; set; }

    /// <summary>
    /// Erstkontakt-Zeitpunkt dieses Geräts (Issue #59/#60) - Grundlage für
    /// <see cref="HaelpMi.Core.Licensing.LicenseLimitEvaluator"/>, da ohne zentrale Instanz nur eine
    /// stabile, von jedem Gerät gleich ermittelbare Reihenfolge entscheiden kann, welche
    /// Geräte innerhalb des Lizenzkontingents liegen. Wird vom Installer NICHT gesetzt
    /// (settings.json entsteht dort als reines Pascal-Script, siehe
    /// HaelpMiCommon.iss.inc) - stattdessen heilt <see cref="Storage.SettingsStore.Load"/>
    /// einen fehlenden Wert beim allerersten App-Start selbst (Default <c>default</c> =
    /// "noch nie gesetzt").
    /// </summary>
    public DateTimeOffset FirstSeenUtc { get; set; }
}
