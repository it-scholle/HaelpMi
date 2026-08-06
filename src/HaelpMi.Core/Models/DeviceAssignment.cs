namespace HaelpMi.Core.Models;

/// <summary>
/// The Admin-assigned facts about one specific device (Pflichtenheft Teil 2, Abschnitt
/// 4: "Raumbezeichnung pro Raum editierbar" - centrally, for any device, not just the
/// device's own owner). Lives inside <see cref="SharedConfig.DeviceAssignments"/>,
/// keyed by device id, and is hot-reloaded down into that device's own
/// <see cref="OwnSettings"/> (Room/RoomNumber/IncomingSoundId) whenever a
/// newer <see cref="SharedConfig"/> arrives - the device's own copy is a cache of this,
/// not a second source of truth.
/// </summary>
public sealed class DeviceAssignment
{
    public string RoomName { get; set; } = string.Empty;

    public string RoomNumber { get; set; } = string.Empty;

    /// <summary>Null = kein per-Gerät-Override, Gerät behält seinen bisherigen Signalton.</summary>
    public string? IncomingSoundId { get; set; }
}
