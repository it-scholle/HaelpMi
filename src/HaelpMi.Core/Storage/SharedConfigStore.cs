using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>Loads/saves the locally cached copy of the centrally admin-owned <see cref="SharedConfig"/> (Teil 2, Abschnitt 10).</summary>
public sealed class SharedConfigStore
{
    public SharedConfig LoadOrCreate()
    {
        var config = JsonFileStore.Load<SharedConfig>(AppPaths.SharedConfigFilePath) ?? new SharedConfig();
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
}
