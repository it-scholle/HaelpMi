namespace HaelpMi.Installer.Tests;

/// <summary>
/// Findet die tatsächlich installierten Programm-/Datenverzeichnisse und die zuletzt
/// gebauten Installer-Dateien (versionsunabhängig - die Dateinamen tragen die
/// {#MyAppVersion}, die sich bei jedem Release ändert). Diese Tests bauen die Installer
/// NICHT selbst (das ist ein separater, langsamer ISCC-Schritt, siehe README.md) -
/// sie erwarten fertige Dateien in installer\Output\, genau wie eine echte
/// Build-Pipeline: erst `dotnet publish` + ISCC, dann dieser Testlauf.
/// </summary>
internal static class InstallerPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string InstallerOutputDir => Path.Combine(RepoRoot, "installer", "Output");

    public static string ProgramFilesInstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HaelpMi");

    public static string ProgramDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HaelpMi");

    // DefaultGroupName="HälpMi" + DisableProgramGroupPage=yes in HaelpMi.iss/HaelpMi-Admin.iss
    // (kein wählbarer Gruppenname) - {group} in [Icons] löst sich damit immer hierhin auf.
    public static string StartMenuGroupDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "HälpMi");

    public static string SettingsJsonPath => Path.Combine(ProgramDataDir, "settings.json");

    public static string DeploymentJsonPath => Path.Combine(ProgramFilesInstallDir, "deployment.json");

    public static string? FindUserInstaller() => FindNewest("HaelpMi-Setup-*.exe", excludeAdmin: true);

    public static string? FindAdminInstaller() => FindNewest("HaelpMi-Setup-Admin-*.exe", excludeAdmin: false);

    public static string? FindUninstaller()
    {
        var path = Path.Combine(ProgramFilesInstallDir, "unins000.exe");
        return File.Exists(path) ? path : null;
    }

    private static string? FindNewest(string pattern, bool excludeAdmin)
    {
        if (!Directory.Exists(InstallerOutputDir))
        {
            return null;
        }

        var candidates = Directory.GetFiles(InstallerOutputDir, pattern)
            .Where(f => !excludeAdmin || !Path.GetFileName(f).Contains("-Admin-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        return candidates.FirstOrDefault();
    }

    // Läuft von tests\HaelpMi.Installer.Tests\bin\<config>\net8.0-windows\ aus - vier
    // Ebenen hoch zum Repo-Root (dem Ordner mit HaelpMi.sln), analog zu TestSupport.cs
    // in HaelpMi.Core.Tests, nur eben ohne Abhängigkeit auf HaelpMi.Core.
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HaelpMi.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("HaelpMi.sln nicht gefunden - Repo-Root-Suche ausgehend von " + AppContext.BaseDirectory + " fehlgeschlagen.");
    }
}
