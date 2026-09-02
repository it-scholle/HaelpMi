using System.IO;
using System.Threading.Tasks;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Verschiebt einen frisch gebauten Admin-Installer auf das Netzlaufwerk Z: (siehe
/// SharedLogPaths/CrashLogger für das gleiche Z:\-Muster bei Logs). Von Toast und
/// dauerhaftem Banner gemeinsam genutzt, damit beide "moveToMount"-Buttons dasselbe
/// Zielverzeichnis und dieselbe Überschreiben-Regel verwenden.
/// </summary>
internal static class InstallerMountMover
{
    private static readonly string TargetDir = Path.Combine("Z:\\", "HaelpMi-Installer");

    // Läuft über Task.Run auf einem Threadpool-Thread: Z: ist ein Netzlaufwerk, ein
    // hängender/getrennter Mount würde File.Move sonst direkt auf dem UI-Thread blockieren
    // und den Install-Creator einfrieren lassen.
    public static Task<string> MoveToMountAsync(string sourcePath)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(TargetDir);
            var targetPath = Path.Combine(TargetDir, Path.GetFileName(sourcePath));
            File.Move(sourcePath, targetPath, overwrite: true);
            return targetPath;
        });
    }
}
