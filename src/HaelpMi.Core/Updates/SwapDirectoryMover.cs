using HaelpMi.Core.Models;

namespace HaelpMi.Core.Updates;

/// <summary>
/// DEPRECATED für release-1.0-MVP: der einzige Aufrufer (<c>UpdateServiceWorker.
/// ConfirmSwapAsync</c>) läuft auf diesem Release-Zweig nicht (Dienst wird nicht gebaut).
///
/// Reine Datei-Verschiebe-Logik für den 0-Downtime-Swap (Abschnitt 11), aus
/// <c>HaelpMi.UpdateService.UpdateServiceWorker.ConfirmSwapAsync</c> herausgezogen, damit
/// sie ohne laufenden SYSTEM-Dienst testbar ist (reine <see cref="System.IO"/>-Operationen
/// auf beliebigen Verzeichnissen, kein Windows-Service-Kontext nötig).
/// </summary>
public static class SwapDirectoryMover
{
    /// <summary>
    /// Verschiebt alle Einträge aus <paramref name="sourceDir"/> nach
    /// <paramref name="destinationDir"/>, außer den in <paramref name="exclude"/> genannten
    /// (Top-Level-Namen, kein Pfad).
    /// </summary>
    public static void MoveAllEntries(string sourceDir, string destinationDir, IReadOnlyCollection<string> exclude)
    {
        foreach (var entry in Directory.GetFileSystemEntries(sourceDir))
        {
            var name = Path.GetFileName(entry);
            if (exclude.Contains(name))
            {
                continue;
            }

            var destination = Path.Combine(destinationDir, name);
            if (Directory.Exists(entry))
            {
                Directory.Move(entry, destination);
            }
            else
            {
                File.Move(entry, destination, overwrite: true);
            }
        }
    }

    /// <summary>
    /// Welche Top-Level-Einträge im Installationsverzeichnis beim "alte Version raus"-Schritt
    /// NICHT nach <c>_previous</c> verschoben (und damit gleich darauf mit gelöscht) werden
    /// dürfen: die Update-Verwaltung selbst (<c>versions</c>/<c>_previous</c>) und alle
    /// installer-eigenen Dateien (<see cref="AppConstants.InstallerOwnedFileNames"/>), die
    /// im generischen P2P-Update-Paket nie enthalten sind und deshalb sonst ersatzlos
    /// verloren gingen (Bugfix 09.08.2026, siehe AppConstants-Kommentar).
    /// </summary>
    public static string[] BuildAppRootMoveExcludeList() =>
        new[] { "versions", "_previous" }.Concat(AppConstants.InstallerOwnedFileNames).ToArray();
}
