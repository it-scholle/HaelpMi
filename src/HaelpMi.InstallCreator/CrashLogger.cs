using System.IO;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Eigenständige Kopie von HaelpMi.Core.Diagnostics.CrashLogger, bewusst nicht von dort
/// referenziert (siehe Kommentar in HaelpMi.InstallCreator.csproj: dieses Tool bleibt
/// absichtlich ohne ProjectReference auf HaelpMi.Core). Läuft nie beim Kunden, daher eigener,
/// einfacher Log-Ort statt des geteilten %ProgramData%\HaelpMi - dieser Ordner existiert auf
/// einem Entwickler-Rechner ohne Installation gar nicht.
/// </summary>
internal static class CrashLogger
{
    public static void InstallProcessWideHooks()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log("AppDomain.UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    public static void Log(string source, Exception ex)
    {
        try
        {
            var dir = ResolveLogDirectory();
            var entry = $"{DateTimeOffset.UtcNow:O}\t{source}\t{ex}{Environment.NewLine}{new string('-', 40)}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, "crash.log"), entry);
        }
        catch (Exception)
        {
            // Das Loggen selbst darf nie eine weitere, die ursprüngliche Ausnahme
            // verdeckende Ausnahme werfen.
        }
    }

    // Gleiches Alpha-Zwischenmuster wie HaelpMi.Core.Diagnostics.CrashLogger: geteiltes
    // Z:-Laufwerk zuerst versuchen (ein Fehlerprotokoll pro Rechner, unabhängig von der
    // App), sonst lokaler Fallback wie bisher.
    private static string ResolveLogDirectory()
    {
        var sharedDir = Path.Combine(@"Z:\", "HaelpMi-Logs", Environment.MachineName);
        try
        {
            Directory.CreateDirectory(sharedDir);
            return sharedDir;
        }
        catch (Exception)
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HaelpMi-InstallCreator");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
