using System.Text.Json;
using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>Loads/saves the locally cached copy of the centrally admin-owned <see cref="SharedConfig"/> (Teil 2, Abschnitt 10).</summary>
public sealed class SharedConfigStore
{
    public SharedConfig LoadOrCreate()
    {
        var config = JsonFileStore.Load<SharedConfig>(AppPaths.SharedConfigFilePath) ?? new SharedConfig();
        MigrateLegacyBridgeSeedAddress(config);
        EnsureBuiltInAllDevicesGroup(config);
        return config;
    }

    public void Save(SharedConfig config) => JsonFileStore.Save(AppPaths.SharedConfigFilePath, config);

    // "Alle"-Gruppe (Nutzerwunsch 09.08.2026): statt über Config-Sync verteilt zu werden,
    // ergänzt jedes Gerät den fest verdrahteten Eintrag (AppConstants.AllDevicesGroupId) hier
    // lokal bei jedem Laden - so kommt auch ein ganz frisches, noch nie synchronisiertes Gerät
    // unabhängig und ohne Admin-Mitwirkung auf dieselbe Gruppe. Rein in-memory (kein Save()
    // hier): löst ConfigSyncService.PublishAsync trotzdem eine echte Speicherung/Verteilung
    // aus (es lädt über LoadOrCreate), zementiert den Eintrag dann auch im synchronisierten
    // Stand für alle. Mitgliedschaft kommt ohnehin nie aus DeviceIds (siehe RecipientResolver),
    // daher reicht das bloße Vorhandensein.
    private static void EnsureBuiltInAllDevicesGroup(SharedConfig config)
    {
        if (config.DeviceGroups.Any(g => g.Id == AppConstants.AllDevicesGroupId))
        {
            return;
        }

        config.DeviceGroups.Insert(0, new DeviceGroup { Id = AppConstants.AllDevicesGroupId, Name = "Alle" });
    }

    // Bridge-Seed-Backfill (Prio 0.1, 15.08.2026): Formatänderung v0.28.0 (cf6b252) von
    // string? BridgeSeedAddress auf List<string> BridgeSeedAddresses - System.Text.Json
    // ignoriert den alten, jetzt unbekannten Schlüssel beim Deserialisieren stillschweigend,
    // die neue Liste bliebe sonst leer und ein zwischen v0.23.0 und v0.27.0 vom Admin
    // gesetzter Wert wäre kommentarlos weg. Gleiches In-Memory-Backfill-Muster wie
    // EnsureBuiltInAllDevicesGroup oben (kein Save() hier - der nächste echte Save via
    // ConfigSyncService.PublishAsync zementiert das Ergebnis für alle): nur wenn die neue
    // Liste noch leer ist, wird die Datei ein zweites Mal roh geparst und nach dem alten
    // Schlüssel gesucht. Rettet nur Geräte, deren Datei seit dem Update auf v0.28.0 noch
    // nicht durch einen echten Save() im neuen Format überschrieben wurde - für bereits
    // überschriebene Dateien ist der alte Wert unwiederbringlich weg, das kann diese
    // Methode nicht nachträglich reparieren.
    private static void MigrateLegacyBridgeSeedAddress(SharedConfig config)
    {
        if (config.BridgeSeedAddresses.Count > 0 || !File.Exists(AppPaths.SharedConfigFilePath))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SharedConfigFilePath));
            if (doc.RootElement.TryGetProperty("BridgeSeedAddress", out var legacy) &&
                legacy.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(legacy.GetString()))
            {
                config.BridgeSeedAddresses.Add(legacy.GetString()!);
            }
        }
        catch (IOException)
        {
            // best-effort, siehe JsonFileStore-Kommentar zu parallelen Schreibzugriffen -
            // beim nächsten LoadOrCreate() wird es erneut versucht.
        }
        catch (JsonException)
        {
            // Datei gerade mitten in einem parallelen Save() erwischt - ebenfalls best-effort.
        }
    }
}
