using System.IO;

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

    public static string MoveToMount(string sourcePath)
    {
        Directory.CreateDirectory(TargetDir);
        var targetPath = Path.Combine(TargetDir, Path.GetFileName(sourcePath));
        File.Move(sourcePath, targetPath, overwrite: true);
        return targetPath;
    }
}
