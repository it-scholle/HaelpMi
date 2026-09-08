namespace HaelpMi.Core.Storage;

/// <summary>Ein per "Löschen" im Geräte-Tab entferntes Gerät (Issue #61-Nachtrag).</summary>
public sealed record RemovedDeviceEntry(Guid DeviceId, DateTimeOffset RemovedAtUtc);

/// <summary>
/// Tombstone-Liste gelöschter Geräte (Issue #61-Nachtrag, Fehlerbericht "Löschen im
/// Geräte-Tab deaktiviert das Gerät nicht wirklich - es kann weiter propagieren und
/// Alarme senden"): getrennt von <see cref="DeviceStore"/>, weil ein gelöschtes Gerät
/// dort bewusst NICHT mehr auftaucht (Löschen entfernt es aus der Übersicht) - ohne
/// diese separate Liste würde ein erneuter Boot-Call/Gossip des gelöschten Geräts (das
/// von seiner eigenen Löschung ja zunächst nichts weiß) es beim nächsten Upsert einfach
/// wieder unsichtbar in devices.json aufleben lassen. Einmal eingetragen, nie wieder
/// entfernt (kein "Wiederherstellen") - eine echte Neuinstallation vergibt eine neue
/// DeviceId (siehe OwnSettings.DeviceId) und betrifft diesen Eintrag hier gar nicht erst.
/// Wird wie DeviceEntry per Gossip an andere Geräte weitergetragen (siehe
/// DiscoveryService.BuildKnownDevicesSummaryAsync/HandleDatagramAsync), damit sich die
/// Löschung ohne zentrale Instanz im ganzen Kreis durchsetzt, auch auf Admin-Dashboards,
/// die den ursprünglichen "Löschen"-Klick nie gesehen haben.
/// </summary>
public sealed class RemovedDeviceStore
{
    public List<RemovedDeviceEntry> Load() =>
        JsonFileStore.Load<List<RemovedDeviceEntry>>(AppPaths.RemovedDevicesFilePath) ?? new List<RemovedDeviceEntry>();

    public void Save(List<RemovedDeviceEntry> entries) => JsonFileStore.Save(AppPaths.RemovedDevicesFilePath, entries);

    public static bool Contains(List<RemovedDeviceEntry> entries, Guid deviceId) =>
        entries.Any(e => e.DeviceId == deviceId);

    /// <summary>Idempotent - ein bereits eingetragenes Gerät bleibt mit seinem ursprünglichen Zeitstempel stehen.</summary>
    public static void Add(List<RemovedDeviceEntry> entries, Guid deviceId, DateTimeOffset removedAtUtc)
    {
        if (!Contains(entries, deviceId))
        {
            entries.Add(new RemovedDeviceEntry(deviceId, removedAtUtc));
        }
    }
}
