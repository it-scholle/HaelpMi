namespace HaelpMi.Core.Models;

/// <summary>
/// The one piece of configuration state that is centrally admin-owned and hot-reload
/// synced to every device (Pflichtenheft Teil 2, Abschnitt 10) - as opposed to
/// <see cref="OwnSettings"/>, which is purely local, non-synced, per-device state.
/// Every device (Admin and User role alike) keeps its own on-disk copy, kept current
/// via <c>ConfigSyncService</c>; only Admin-role devices ever originate a change to it.
///
/// <see cref="ConfigVersion"/> is compared, never trusted blindly: a device only
/// applies an incoming <see cref="SharedConfig"/> if its version is strictly newer than
/// what it already has (Config-Sync broadcast) or reports its own version back
/// (Boot-Call) so the other side can tell who is behind.
/// </summary>
public sealed class SharedConfig
{
    public int ConfigVersion { get; set; }

    public List<DeviceGroup> DeviceGroups { get; set; } = new();

    public List<AlarmProfile> AlarmProfiles { get; set; } = new();

    public Dictionary<Guid, DeviceAssignment> DeviceAssignments { get; set; } = new();

    /// <summary>Admin-gesteuerte Freigabe/Staffelung für Programm-Updates (Abschnitt 11).</summary>
    public UpdateRolloutState UpdateRollout { get; set; } = new();
}

/// <summary>
/// "Rollout ist gestaffelt und wird vom Admin freigegeben, nicht unkontrolliert
/// lauffeuerartig" (CLAUDE.md). Kein aktives Rollout (<see cref="ApprovedVersion"/> leer)
/// heißt: kein Gerät darf über die Update-Pipeline automatisch aktualisieren, ganz gleich
/// welche neuere Version es bei einem Peer sieht.
/// </summary>
public sealed class UpdateRolloutState
{
    public string? ApprovedVersion { get; set; }

    /// <summary>
    /// Wie viele Geräte insgesamt (kundengruppenweit) gerade aktualisieren dürfen. Admin
    /// steigert schrittweise (Beispiel aus Abschnitt 11: 1 -&gt; 2 -&gt; 4 -&gt; 8); welche
    /// konkreten Geräte "die ersten N" sind, wird deterministisch über eine stabile
    /// Sortierung der Geräte-IDs entschieden (siehe UpdateOrchestrator), nicht über eine
    /// vom Admin einzeln kuratierte Geräteliste. War früher pro Kreis gestaffelt - mit dem
    /// Wegfall des Kreis-Konzepts (04.08.2026) global vereinfacht.
    /// </summary>
    public int ApprovedDeviceQuota { get; set; }
}
