namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Ein "Endgültig löschen"-Tombstone aus <see cref="Storage.RemovedDeviceStore"/> im
/// Gossip-Anhang eines Boot-Calls (Bugfix "gelöschtes Gerät taucht per Gossip wieder auf"
/// 10.09.2026). Bewusst NICHT über <see cref="KnownDeviceSummary.Removed"/> transportiert:
/// dieses Feld trägt auch die weiche, umkehrbare "Deinstalliert"-Markierung
/// (<see cref="Models.DeviceEntry.Removed"/>) eines ganz normalen DeviceEntry, die ein
/// Empfänger bewusst NICHT in seinen eigenen <see cref="Storage.RemovedDeviceStore"/>
/// übernehmen darf (siehe DiscoveryService.HandleDatagramAsync). Ohne dieses eigene Feld
/// ließ sich auf dem Draht nicht unterscheiden, ob ein Removed=true-Eintrag ein endgültiger
/// Tombstone oder nur eine weiche Selbst-/Fremdauskunft war - ein endgültig gelöschtes
/// Gerät lebte dadurch bei jedem Peer wieder auf, der es direkt kontaktierte, bevor er den
/// (bisher als KnownDeviceSummary getarnten) Tombstone erhalten hatte.
/// </summary>
public sealed record PermanentlyRemovedDeviceSummary(Guid DeviceId, DateTimeOffset RemovedAtUtc);
