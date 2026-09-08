using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>
/// The subset of a boot-call/announce that gets written into a <see cref="DeviceEntry"/> on upsert.
/// <paramref name="FirstSeenUtc"/> ist der vom meldenden Gerät selbst behauptete Erstkontakt-
/// Zeitpunkt (siehe <see cref="DeviceEntry.FirstSeenUtc"/>) - null bei einem Absender ohne
/// dieses Feld (ältere Programmversion mitten in einem Rollout), dann fällt Upsert auf den
/// lokalen Empfangszeitpunkt zurück.
///
/// <paramref name="Override"/>/<paramref name="OverrideSetAtUtc"/> (Issue #61): eine
/// Fremdmeinung über <see cref="DeviceEntry.LicenseOverride"/, nur aus Gossip-Drittwissen
/// (<see cref="Protocol.KnownDeviceSummary"/>) befüllt - ein Gerät berichtet nie über sich
/// selbst, deshalb bleibt dieses Feld beim direkten Selbstbericht des betroffenen Geräts
/// null ("nicht anfassen"). Siehe <see cref="Upsert"/> für die Merge-Regel.
///
/// <paramref name="Removed"/>/<paramref name="RemovedSetAtUtc"/> (Issue #61-Nachtrag
/// 08.09.2026 "Deinstalliert" statt Verstecken): dieselbe Fremdmeinungs-Semantik wie
/// <paramref name="Override"/>, nur für <see cref="DeviceEntry.Removed"/> - ebenfalls nie
/// aus einem Selbstbericht befüllt (ein Gerät erfährt seine eigene Deinstallation nie von
/// sich selbst, siehe <see cref="Networking.DiscoveryService.AnnounceSelfRemovedAsync"/> für
/// den Sonderweg, über den ein Gerät das trotzdem über sich selbst verbreiten kann).
///
/// <paramref name="LastInstalledAtUtc"/> (Issue #61-Nachtrag): GENAU umgekehrt - ausschließlich
/// aus einem Selbstbericht befüllt (nur das betroffene Gerät selbst weiß, wann es zuletzt
/// installiert/repariert wurde, siehe OwnSettings.LastInstalledAtUtc), nie aus Gossip über
/// ein drittes Gerät. Überbietet ein vorhandenes <see cref="DeviceEntry.RemovedSetAtUtc"/>,
/// hebt die Markierung dann automatisch auf - "wird das Gerät neu installiert, wird der Tag
/// einfach entfernt" (Nutzerwunsch 08.09.2026).
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
    DateTimeOffset? FirstSeenUtc,
    LicenseOverride? Override = null,
    DateTimeOffset? OverrideSetAtUtc = null,
    bool? Removed = null,
    DateTimeOffset? RemovedSetAtUtc = null,
    DateTimeOffset? LastInstalledAtUtc = null);

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
                FirstSeenUtc = info.FirstSeenUtc ?? seenAtUtc,
                IsNew = true,
                LicenseOverride = info.Override ?? LicenseOverride.None,
                LicenseOverrideSetAtUtc = info.Override is not null ? info.OverrideSetAtUtc : null,
                Removed = info.Removed ?? false,
                RemovedSetAtUtc = info.Removed is not null ? info.RemovedSetAtUtc : null,
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
            // FirstSeenUtc nur nach UNTEN korrigieren, nie nach oben: eine dritte Quelle
            // (Gossip) kann einen echten, früheren Zeitpunkt nachliefern als die bisher
            // gespeicherte Schätzung (z. B. lokaler Empfangszeitpunkt-Fallback beim
            // allerersten Kontakt) - niemals umgekehrt, sonst würde ein länger laufendes
            // Gerät nachträglich als "neuer" umgewertet (Fairness-Grundlage für
            // LicenseLimitEvaluator, Issue #59/#60).
            if (info.FirstSeenUtc is { } reportedFirstSeen && reportedFirstSeen < existing.FirstSeenUtc)
            {
                existing.FirstSeenUtc = reportedFirstSeen;
            }

            // LicenseOverride (Issue #61): "neuester Zeitstempel gewinnt" - übernimmt eine
            // per Gossip gemeldete Fremdmeinung (info.Override != null, siehe DeviceUpsertInfo)
            // nur, wenn sie neuer ist als die lokal bekannte Entscheidung, nie umgekehrt.
            // Ein Selbstbericht (info.Override == null) lässt den lokalen Stand unangetastet.
            if (info.Override is { } reportedOverride
                && (existing.LicenseOverrideSetAtUtc is null || info.OverrideSetAtUtc > existing.LicenseOverrideSetAtUtc))
            {
                existing.LicenseOverride = reportedOverride;
                existing.LicenseOverrideSetAtUtc = info.OverrideSetAtUtc;
            }

            // Removed (Issue #61-Nachtrag): dieselbe "neuester Zeitstempel gewinnt"-Regel
            // wie beim Override - eine Fremdmeinung (info.Removed != null) übernimmt sowohl
            // eine neu gemeldete Deinstallation ALS AUCH eine neu gemeldete Aufhebung
            // (Removed:false mit neuerem Zeitstempel), falls sie neuer ist als der lokal
            // bekannte Stand.
            if (info.Removed is { } reportedRemoved
                && (existing.RemovedSetAtUtc is null || info.RemovedSetAtUtc > existing.RemovedSetAtUtc))
            {
                existing.Removed = reportedRemoved;
                existing.RemovedSetAtUtc = info.RemovedSetAtUtc;
            }

            // LastInstalledAtUtc (Issue #61-Nachtrag, ausschließlich Selbstbericht - siehe
            // DeviceUpsertInfo): eine neuere eigene Installation als die zuletzt bekannte
            // Removed-Entscheidung hebt diese automatisch auf - "Neuinstallation entfernt
            // den Delete-Tag" (Nutzerwunsch), ohne dass irgendjemand das Gerät erst wieder
            // manuell aktivieren müsste.
            if (existing.Removed && info.LastInstalledAtUtc is { } lastInstalledAtUtc
                && lastInstalledAtUtc > (existing.RemovedSetAtUtc ?? DateTimeOffset.MinValue))
            {
                existing.Removed = false;
                existing.RemovedSetAtUtc = null;
            }
            // Favorite/Notified/Note/IsNew/LicenseLimitWarningAcknowledged sind lokale Entscheidungen und bleiben unangetastet.
        }

        return devices;
    }

    /// <summary>
    /// Lokale Admin-Entscheidung im Geräte-Tab (Issue #61) - setzt Override + Zeitstempel
    /// unbedingt (ein bewusster Klick des hiesigen Admins gewinnt immer lokal); die
    /// Verbreitung an Peers läuft danach über den normalen Gossip-Pfad
    /// (<see cref="Protocol.KnownDeviceSummary"/>, siehe DiscoveryService).
    /// </summary>
    public static void SetLicenseOverride(List<DeviceEntry> devices, Guid deviceId, LicenseOverride value, DateTimeOffset setAtUtc)
    {
        var entry = devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (entry is not null)
        {
            entry.LicenseOverride = value;
            entry.LicenseOverrideSetAtUtc = setAtUtc;
        }
    }

    /// <summary>
    /// "Endgültig löschen" im Geräte-Tab (Issue #61-Nachtrag 08.09.2026 - vor der
    /// Neufassung schlicht "Löschen"): rein lokal, die eigentliche Verbreitung/
    /// Resurrection-Sperre läuft über <see cref="RemovedDeviceStore"/> (siehe dortiger
    /// Kommentar) - anders als die sichtbare, umkehrbare <see cref="DeviceEntry.Removed"/>-
    /// Markierung ("Deinstalliert") ist das hier die bewusst unumkehrbare, admin-only
    /// Aktion: das Gerät verschwindet komplett aus der Übersicht, eine spätere
    /// Neuinstallation hebt das NICHT automatisch wieder auf.
    /// </summary>
    public static void Remove(List<DeviceEntry> devices, Guid deviceId) =>
        devices.RemoveAll(d => d.DeviceId == deviceId);

    /// <summary>
    /// "Als deinstalliert markieren" im Geräte-Tab (Issue #61-Nachtrag) - manuelle
    /// Rückfallebene, falls die automatische Deinstallations-Meldung
    /// (<see cref="Networking.DiscoveryService.AnnounceSelfRemovedAsync"/>) niemanden
    /// erreicht hat. Setzt <see cref="DeviceEntry.Removed"/>/<see cref="DeviceEntry.RemovedSetAtUtc"/>
    /// unbedingt (ein bewusster Admin-Klick gewinnt immer lokal), Verbreitung läuft danach
    /// wie beim Override über den normalen Gossip-Pfad.
    /// </summary>
    public static void SetRemoved(List<DeviceEntry> devices, Guid deviceId, bool removed, DateTimeOffset setAtUtc)
    {
        var entry = devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (entry is not null)
        {
            entry.Removed = removed;
            entry.RemovedSetAtUtc = setAtUtc;
        }
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

    /// <summary>"Gelesen" im Admin-Dashboard-Lizenzlimit-Banner (Issue #60) - siehe DeviceEntry.LicenseLimitWarningAcknowledged.</summary>
    public static void AcknowledgeLicenseLimitWarning(List<DeviceEntry> devices, Guid deviceId)
    {
        var entry = devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (entry is not null)
        {
            entry.LicenseLimitWarningAcknowledged = true;
        }
    }

    /// <summary>Ordering for the Individuell list: favorites first (FR-19), then by computer name.</summary>
    public static IEnumerable<DeviceEntry> OrderForDisplay(IEnumerable<DeviceEntry> devices) =>
        devices.OrderByDescending(d => d.Favorite).ThenBy(d => d.ComputerName, StringComparer.CurrentCultureIgnoreCase);
}
