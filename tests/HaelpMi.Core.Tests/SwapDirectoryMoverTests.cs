using HaelpMi.Core.Updates;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Bugfix 09.08.2026: der 0-Downtime-Swap (Abschnitt 11) verschob bisher ausnahmslos ALLE
/// Einträge des Installationsverzeichnisses nach <c>_previous</c> und löschte diese
/// anschließend - darunter auch <c>deployment.json</c>, obwohl das generisch gepullte
/// Update-Paket sie nie enthält. Ergebnis: die App startete nach jedem Swap gar nicht mehr
/// (<c>DeploymentInfoStore.Load</c> wirft beim Fehlen). Diese Tests decken die daraus
/// ausgelagerte, ohne SYSTEM-Dienst testbare Verschiebe-Logik ab.
/// </summary>
public class SwapDirectoryMoverTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "haelpmi-swap-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void MoveAllEntries_LeavesExcludedFilesInSource()
    {
        var source = CreateTempDir();
        var destination = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(source, "deployment.json"), "{}");
            File.WriteAllText(Path.Combine(source, "HaelpMi.Agent.exe"), "fake-exe");
            Directory.CreateDirectory(Path.Combine(source, "versions"));

            SwapDirectoryMover.MoveAllEntries(source, destination, exclude: SwapDirectoryMover.BuildAppRootMoveExcludeList());

            Assert.True(File.Exists(Path.Combine(source, "deployment.json"))); // ausgeschlossen - bleibt im Quellverzeichnis
            Assert.True(Directory.Exists(Path.Combine(source, "versions"))); // ausgeschlossen - bleibt ebenfalls
            Assert.False(File.Exists(Path.Combine(destination, "deployment.json")));

            Assert.True(File.Exists(Path.Combine(destination, "HaelpMi.Agent.exe"))); // nicht ausgeschlossen - wird verschoben
            Assert.False(File.Exists(Path.Combine(source, "HaelpMi.Agent.exe")));
        }
        finally
        {
            Directory.Delete(source, true);
            Directory.Delete(destination, true);
        }
    }

    [Fact]
    public void BuildAppRootMoveExcludeList_ContainsInstallerOwnedFilesAndVersionFolders()
    {
        var exclude = SwapDirectoryMover.BuildAppRootMoveExcludeList();

        Assert.Contains("versions", exclude);
        Assert.Contains("_previous", exclude);
        Assert.Contains("deployment.json", exclude);
        Assert.Contains("HaelpMi-User-Setup.exe", exclude);
    }

    [Fact]
    public void MoveAllEntries_MovesNonExcludedSubdirectoriesToo()
    {
        var source = CreateTempDir();
        var destination = CreateTempDir();
        try
        {
            var subDir = Path.Combine(source, "runtimes");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "lib.dll"), "fake-dll");

            SwapDirectoryMover.MoveAllEntries(source, destination, exclude: Array.Empty<string>());

            Assert.True(File.Exists(Path.Combine(destination, "runtimes", "lib.dll")));
            Assert.False(Directory.Exists(subDir));
        }
        finally
        {
            Directory.Delete(source, true);
            Directory.Delete(destination, true);
        }
    }
}
