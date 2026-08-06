using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>Loads/saves the locally cached copy of the centrally admin-owned <see cref="SharedConfig"/> (Teil 2, Abschnitt 10).</summary>
public sealed class SharedConfigStore
{
    public SharedConfig LoadOrCreate() =>
        JsonFileStore.Load<SharedConfig>(AppPaths.SharedConfigFilePath) ?? new SharedConfig();

    public void Save(SharedConfig config) => JsonFileStore.Save(AppPaths.SharedConfigFilePath, config);
}
