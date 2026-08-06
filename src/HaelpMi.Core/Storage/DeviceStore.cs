using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>The subset of a boot-call/announce that gets written into a <see cref="DeviceEntry"/> on upsert.</summary>
public sealed record DeviceUpsertInfo(
    string ComputerName,
    string User,
    string RoomName,
    string RoomNumber,
    Role Role,
    bool IsRemoteSession,
    string IpAddress,
    int TcpPort);

/// <summary>Loads/saves the locally known list of other devices (FR-18, 5.4).</summary>
public sealed class DeviceStore
{
    public List<DeviceEntry> Load() =>
        JsonFileStore.Load<List<DeviceEntry>>(AppPaths.DevicesFilePath) ?? new List<DeviceEntry>();

    public void Save(List<DeviceEntry> devices) => JsonFileStore.Save(AppPaths.DevicesFilePath, devices);

    /// <summary>
    /// Adds a newly-seen device or refreshes an already-known one, matched by
    /// <see cref="DeviceEntry.DeviceId"/> - never by IP (FR-22) - so a DHCP-driven IP
    /// change doesn't create a duplicate row and doesn't touch Favorite/Notified/Note.
    /// Returns the updated list; caller is responsible for persisting it.
    /// </summary>
    public static List<DeviceEntry> Upsert(List<DeviceEntry> devices, Guid deviceId, DeviceUpsertInfo info, DateTimeOffset seenAtUtc)
    {
        var existing = devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (existing is null)
        {
            devices.Add(new DeviceEntry
            {
                DeviceId = deviceId,
                ComputerName = info.ComputerName,
                User = info.User,
                RoomName = info.RoomName,
                RoomNumber = info.RoomNumber,
                Role = info.Role,
                IsRemoteSession = info.IsRemoteSession,
                IpAddress = info.IpAddress,
                TcpPort = info.TcpPort,
                LastSeenUtc = seenAtUtc,
                IsNew = true,
            });
        }
        else
        {
            existing.ComputerName = info.ComputerName;
            existing.User = info.User;
            existing.RoomName = info.RoomName;
            existing.RoomNumber = info.RoomNumber;
            existing.Role = info.Role;
            existing.IsRemoteSession = info.IsRemoteSession;
            existing.IpAddress = info.IpAddress;
            existing.TcpPort = info.TcpPort;
            existing.LastSeenUtc = seenAtUtc;
            // Favorite/Notified/Note/IsNew are local decisions and are deliberately left untouched.
        }

        return devices;
    }

    /// <summary>
    /// Clears the "Neu" highlight once the Notified checkbox has been consciously set or
    /// left as-is for this device (FR-25). The Konfigurationsfenster calls this on every
    /// checkbox interaction, including a no-op "leave it" click.
    /// </summary>
    public static void AcknowledgeNewState(List<DeviceEntry> devices, Guid deviceId)
    {
        var entry = devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (entry is not null)
        {
            entry.IsNew = false;
        }
    }

    /// <summary>Ordering for the Individuell list: favorites first (FR-19), then by computer name.</summary>
    public static IEnumerable<DeviceEntry> OrderForDisplay(IEnumerable<DeviceEntry> devices) =>
        devices.OrderByDescending(d => d.Favorite).ThenBy(d => d.ComputerName, StringComparer.CurrentCultureIgnoreCase);
}
