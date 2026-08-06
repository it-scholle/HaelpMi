using System.Text.Json;

namespace HaelpMi.Core.Storage;

/// <summary>
/// Minimal atomic JSON read/write for a single file: writes go to a temp file first,
/// then File.Replace, so a crash or concurrent Config/Agent write never leaves a
/// half-written file behind. Both HaelpMi.Agent and HaelpMi.Config independently load
/// and save through this, so callers should re-Load() before acting on shared state
/// rather than assuming their in-memory copy is still current.
/// </summary>
public static class JsonFileStore
{
    // Agent and Config are separate processes that both touch the same file (settings.json
    // or devices.json) with no shared lock - a brief IOException from the other side mid-
    // write (or AV real-time scanning holding a newly-written file a moment longer) is
    // expected, not exceptional. 30x100ms gives a generous 3s window before giving up -
    // it must stay generous, because giving up too early on Load() is worse than it looks:
    // SettingsStore.Load() treats a null result as "installation broken/incomplete" and
    // throws rather than silently continuing (see AppConstants/FR-3 - regenerating the
    // device id by accident is an explicitly named known risk in the Pflichtenheft, 6.).
    private const int MaxRetryAttempts = 30;
    private const int RetryDelayMilliseconds = 100;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return JsonSerializer.Deserialize<T>(stream, Options);
            }
            catch (IOException) when (attempt < MaxRetryAttempts)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
    }

    public static void Save<T>(string path, T value) where T : class
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");

        using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Options);
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
                return;
            }
            catch (IOException) when (attempt < MaxRetryAttempts)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
    }
}
