using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>
/// The subset of a boot-call/announce that gets written into a <see cref="DeviceEntry"/> on
/// upsert. <see cref="ReportedLastSeenUtc"/> (Nutzerwunsch 15.08.2026): nur bei einem
/// gossip-gelernten Eintrag gesetzt (der Zeitpunkt, zu dem der Informant es zuletzt selbst
/// gesehen hat) - bei direktem Kontakt bleibt es null, dort ist "jetzt" (der seenAtUtc-
/// Parameter von Upsert) weiterhin die genaueste verfügbare Angabe.
///
/// <see cref="ObservedProtocolVersion"/>/<see cref="PinnedDeviceIdentityPublicKeyBase64"/>
/// (LAN-Verschlüsselung, siehe CLAUDE.md "Lizenz &amp; Secrets"): <c>null</c> = "nicht
/// anfassen" - beide werden ausschließlich bei direktem Boot-Call-Kontakt explizit gesetzt
/// (siehe DiscoveryService.HandleDatagramAsync), nie aus dem Gossip-Pfad übernommen,
/// gleiches Prinzip wie beim Admin-Rollen-Nachweis. Ein gossip-gelernter Eintrag bleibt
/// deshalb bis zum ersten eigenen direkten Kontakt konsequent "nicht verschlüsselungsfähig"
/// (sicherer Standardfall, siehe PeerCryptoCapability).
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
    int? ObservedProtocolVersion = null,
    string? PinnedDeviceIdentityPublicKeyBase64 = null,
    // Wellen-Rollout (Nutzerwunsch 16.08.2026, siehe DeviceEntry.LastKnownProgramVersion):
    // Default leer statt Pflichtfeld, damit die drei bestehenden AuditSyncTests-Aufrufe
    // (nur direkter Kontakt, kein Interesse an der Programmversion) unverändert bleiben.
    string ProgramVersion = "");

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
        var protocolVersion = info.ObservedProtocolVersion ?? existing?.ProtocolVersion;
        var pinnedKey = info.PinnedDeviceIdentityPublicKeyBase64 ?? existing?.PinnedDeviceIdentityPublicKeyBase64;

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
                IsNew = true,
                ProtocolVersion = protocolVersion,
                PinnedDeviceIdentityPublicKeyBase64 = pinnedKey,
                LastKnownProgramVersion = info.ProgramVersion,
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
            existing.ProtocolVersion = protocolVersion;
            existing.PinnedDeviceIdentityPublicKeyBase64 = pinnedKey;
            // Favorite/Notified/Note/IsNew are local decisions and are deliberately left untouched.
            // Leeres info.ProgramVersion (z. B. ein Aufrufer, der das Feld gar nicht kennt)
            // überschreibt einen schon bekannten Wert nicht rückwärts mit "unbekannt".
            if (!string.IsNullOrWhiteSpace(info.ProgramVersion))
            {
                existing.LastKnownProgramVersion = info.ProgramVersion;
            }
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
