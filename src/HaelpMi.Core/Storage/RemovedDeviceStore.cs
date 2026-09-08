namespace HaelpMi.Core.Storage;

/// <summary>
/// Ein per "Löschen" im Geräte-Tab entferntes Gerät (Issue #61-Nachtrag).
/// <paramref name="LastKnownIpAddress"/> (Issue #61-Nachtrag, Nutzerbericht "Löschen ist
/// ein Freischein" 08.09.2026): die zuletzt bekannte IP des gelöschten Geräts, im Moment
/// des Löschens aus dessen (dann verworfenem) <see cref="DeviceEntry"/> übernommen - einzige
/// Grundlage, über die DiscoveryService.NotifyKnownPeersDirectlyAsync das Gerät noch
/// direkt (statt nur per Broadcast) erreichen kann, da es ja bewusst nicht mehr in
/// devices.json steht. Null, falls beim Löschen keine IP bekannt war (Gerät nie erreicht).
/// </summary>
public sealed record RemovedDeviceEntry(Guid DeviceId, DateTimeOffset RemovedAtUtc, string? LastKnownIpAddress = null);

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

    /// <summary>Idempotent - ein bereits eingetragenes Gerät bleibt mit seinem ursprünglichen Zeitstempel/IP stehen.</summary>
    public static void Add(List<RemovedDeviceEntry> entries, Guid deviceId, DateTimeOffset removedAtUtc, string? lastKnownIpAddress = null)
    {
        if (!Contains(entries, deviceId))
        {
            entries.Add(new RemovedDeviceEntry(deviceId, removedAtUtc, lastKnownIpAddress));
        }
    }
}
