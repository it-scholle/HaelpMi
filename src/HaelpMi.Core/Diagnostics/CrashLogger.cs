using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Diagnostics;

/// <summary>
/// Without this, an unhandled exception can silently kill a process with no trace at
/// all - exactly what makes a bug like "the window stops responding and then just
/// closes" impossible to diagnose after the fact. <see cref="InstallProcessWideHooks"/>
/// wires up the two non-UI-framework places an exception can slip through unobserved;
/// each WPF app additionally hooks its own <c>Application.DispatcherUnhandledException</c>
/// and calls <see cref="Log"/> from there (kept out of this framework-agnostic Core
/// project on purpose - see the class remarks on other Core types for why).
///
/// Logs exception type/message/stack trace only - never any alarm text or device data
/// (NFR-5, Datenminimierung) - a crash trace is diagnostic/operational information, not
/// message content.
/// </summary>
public static class CrashLogger
{
    public static void InstallProcessWideHooks(string processName)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log(processName, "AppDomain.UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log(processName, "TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    public static void Log(string processName, string source, Exception ex)
    {
        try
        {
            var dir = ResolveLogDirectory();
            var entry = $"{DateTimeOffset.UtcNow:O}\t{processName}\t{source}\t{ex}{Environment.NewLine}{new string('-', 40)}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, "crash.log"), entry);
        }
        catch (Exception)
        {
            // Logging the crash must never itself throw and mask the original exception.
        }
    }

    // Alpha-Testphase: geteiltes Z:-Laufwerk, damit Fehlerprotokolle aller Test-VMs an
    // einem Ort landen statt einzeln auf jeder Maschine gesucht werden zu müssen - pro
    // Rechnername ein eigener Unterordner, damit gleichzeitige Schreibzugriffe mehrerer
    // VMs sich nicht in dieselbe Datei mischen. Fällt automatisch auf den bisherigen
    // lokalen %ProgramData%-Pfad zurück, wenn Z: nicht erreichbar ist (z. B. spätere
    // echte Kunden-Installation ohne dieses Laufwerk) - die im CLAUDE.md-Gespräch
    // vorgesehene "nur nach Admin-Bestätigung"-Variante für die Produktivphase ist damit
    // NICHT umgesetzt, nur dieser pragmatische Alpha-Zwischenschritt.
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
            AppPaths.EnsureRootExists();
            return AppPaths.RootFolder;
        }
    }
}
