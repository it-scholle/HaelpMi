using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>
/// The subset of a boot-call/announce that gets written into a <see cref="DeviceEntry"/> on
/// upsert. <see cref="ReportedLastSeenUtc"/> (Nutzerwunsch 15.08.2026): nur bei einem
/// gossip-gelernten Eintrag gesetzt (der Zeitpunkt, zu dem der Informant es zuletzt selbst
/// gesehen hat) - bei direktem Kontakt bleibt es null, dort ist "jetzt" (der seenAtUtc-
/// Parameter von Upsert) weiterhin die genaueste verfügbare Angabe.
///
/// <see cref="AdminVerified"/> (Nutzerwunsch 15.08.2026, Admin-Rollen-Authentifizierung):
/// <c>null</c> = "nicht anfassen" (Gossip-Pfad - eine gossip-gelernte Admin-Behauptung
/// wird nie selbst als verifiziert übernommen). Direkter Kontakt liefert immer explizit
/// <c>true</c> oder <c>false</c> (Ergebnis der Signaturprüfung), auch bei <c>Role !=
/// Admin</c>.
/// </summary>
public sealed record DeviceUpsertInfo(
    string ComputerName,
    string User,
    string RoomName,
    string RoomNumber,
    Role Role,
    bool IsRemoteSession,
    string IpAddress,
    int TcpPort,
    DateTimeOffset? ReportedLastSeenUtc = null,
    bool? AdminVerified = null);

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
        var lastSeenUtc = ResolveLastSeenUtc(existing?.LastSeenUtc, info, seenAtUtc);
        var adminVerified = info.AdminVerified ?? existing?.AdminVerified ?? false;

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
                LastSeenUtc = lastSeenUtc,
                AdminVerified = adminVerified,
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
            existing.LastSeenUtc = lastSeenUtc;
            existing.AdminVerified = adminVerified;
            // Favorite/Notified/Note/IsNew are local decisions and are deliberately left untouched.
        }

        return devices;
    }

    /// <summary>
    /// Direkter Kontakt (<see cref="DeviceUpsertInfo.ReportedLastSeenUtc"/> == null): "jetzt"
    /// ist die genaueste verfügbare Angabe, wie bisher. Gossip-Weitergabe (Nutzerwunsch
    /// 15.08.2026): der Zeitpunkt, zu dem der Informant das Gerät zuletzt SELBST gesehen hat,
    /// ist genauer als "jetzt" (wann WIR vom Gossip gehört haben) - aber nie rückwärts
    /// überschreiben, falls wir das Gerät zwischenzeitlich über einen anderen Weg schon
    /// aktueller gesehen haben.
    /// </summary>
    private static DateTimeOffset ResolveLastSeenUtc(DateTimeOffset? existingLastSeenUtc, DeviceUpsertInfo info, DateTimeOffset seenAtUtc)
    {
        var candidate = info.ReportedLastSeenUtc ?? seenAtUtc;
        return existingLastSeenUtc is { } existing && existing > candidate ? existing : candidate;
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
