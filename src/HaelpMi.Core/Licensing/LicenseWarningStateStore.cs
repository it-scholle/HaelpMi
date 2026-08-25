using System.Text.Json;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Licensing;

/// <summary>Lädt/speichert <see cref="LicenseWarningReminderState"/> unter %ProgramData%\HaelpMi (Issue #20).</summary>
public sealed class LicenseWarningStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public LicenseWarningReminderState Load()
    {
        var path = AppPaths.LicenseWarningStateFilePath;
        if (!File.Exists(path))
        {
            return new LicenseWarningReminderState();
        }

        try
        {
            return JsonSerializer.Deserialize<LicenseWarningReminderState>(File.ReadAllText(path), Options)
                   ?? new LicenseWarningReminderState();
        }
        catch (Exception)
        {
            return new LicenseWarningReminderState(); // beschädigte Datei - verhält sich wie "kein Aufschub gemerkt"
        }
    }

    public void Save(LicenseWarningReminderState state)
    {
        AppPaths.EnsureRootExists();
        File.WriteAllText(AppPaths.LicenseWarningStateFilePath, JsonSerializer.Serialize(state, Options));
    }
}
