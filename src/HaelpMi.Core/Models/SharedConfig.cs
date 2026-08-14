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

    /// <summary>Admin-gesteuerte Freigabe für Programm-Updates (Abschnitt 11).</summary>
    public UpdateRolloutState UpdateRollout { get; set; } = new();

    /// <summary>
    /// Multi-VLAN-Bootstrap-Seed (Nutzerwunsch 13.08.2026, Admin-Gerät "als so eine Art
    /// erster Peer"): IP-Adresse oder Hostname eines Geräts in einem anderen, nur
    /// gerouteten (nicht per Broadcast erreichbaren) Subnetz/VLAN. <see cref="DiscoveryService"/>
    /// unicastet seinen Boot-Call zusätzlich zum lokalen Broadcast an diese Adresse - sobald
    /// der erste Kontakt über die Brücke steht, übernimmt das bestehende Gossip
    /// (<c>KnownDeviceSummary</c> in den Discovery-Replies) die restliche Verteilung, die
    /// Brücke selbst muss danach nicht mehr bestehen bleiben (Ausfalltoleranz).
    ///
    /// Optional, leer = kein Multi-VLAN-Bootstrap konfiguriert (Normalfall: alle Geräte im
    /// selben Subnetz/VLAN, Broadcast reicht). Editierbar im Admin-Dashboard (Netzwerk-Tab,
    /// <see cref="EditScopeKind.NetworkBridge"/>) und hot-reload-verteilt wie jedes andere
    /// Feld hier - ein per Install-Creator vorbelegter Startwert (siehe
    /// <see cref="DeploymentInfo.BridgeSeedAddress"/>) dient nur als Fallback, solange noch
    /// nie ein Config-Sync stattgefunden hat (frisch installiertes Gerät an einer Außenstelle,
    /// das seine erste Config erst über genau diese Brücke ziehen kann).
    /// </summary>
    public string? BridgeSeedAddress { get; set; }
}

/// <summary>
/// "Der Admin gibt das Update genau einmal frei. Ab da verbreitet sich das Update
/// vollautomatisch von Gerät zu Gerät weiter" (CLAUDE.md, Rollout-Freigabe korrigiert
/// 11.08.2026 - ersetzt das frühere gestaffelte Freigabekontingent). Kein aktives Rollout
/// (<see cref="ApprovedVersion"/> leer) heißt: kein Gerät darf über die Update-Pipeline
/// automatisch aktualisieren, ganz gleich welche neuere Version es bei einem Peer sieht.
/// Sobald eine Version hier freigegeben ist, darf jedes Gerät sie ziehen und beim eigenen
/// nächsten Boot-Call weiterverteilen - kein Kontingent, keine Zwischenstufen.
/// </summary>
public sealed class UpdateRolloutState
{
    public string? ApprovedVersion { get; set; }
}
