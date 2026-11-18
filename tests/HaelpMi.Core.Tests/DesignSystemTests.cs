using System.Text.RegularExpressions;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Prüft die Regeln des Design-Systems (AGENTS.md, „Design-System“) direkt am Quelltext:
/// Farben nur aus design/xaml, jeder referenzierte Ressourcenschlüssel existiert, jede Datei
/// in design/icons steht im Katalog. Ein fehlender Schlüssel fiele sonst erst beim Öffnen des
/// Fensters als XamlParseException auf.
/// </summary>
public class DesignSystemTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    // Projekt -> Projekte, deren XAML-Ressourcen zur Laufzeit erreichbar sind (eigenes + referenziertes UI).
    private static readonly Dictionary<string, string[]> ResourceScopes = new()
    {
        ["HaelpMi.UI"] = ["HaelpMi.UI"],
        ["HaelpMi.InstallCreator"] = ["HaelpMi.InstallCreator"],
        ["HaelpMi.Config"] = ["HaelpMi.Config", "HaelpMi.UI"],
        ["HaelpMi.Agent"] = ["HaelpMi.Agent", "HaelpMi.UI"],
    };

    private static readonly Regex HexColor = new(@"(?<!&)#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b", RegexOptions.Compiled);
    private static readonly Regex KeyDefinition = new(@"x:Key=""([A-Za-z0-9_.]+)""", RegexOptions.Compiled);
    private static readonly Regex XamlReference = new(@"\{(?:Static|Dynamic)Resource\s+(?:ResourceKey=)?([A-Za-z0-9_.]+)\s*\}", RegexOptions.Compiled);
    private static readonly Regex CodeReference = new(@"(?:FindResource|TryFindResource)\(""([A-Za-z0-9_.]+)""\)|Resources\[""([A-Za-z0-9_.]+)""\]", RegexOptions.Compiled);

    [Fact]
    public void FensterUndStyles_EnthaltenKeineFestenFarben()
    {
        var offenders = XamlFiles(Path.Combine(RepoRoot, "src"))
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, line, index))
                .Where(x => !x.line.TrimStart().StartsWith("<!--") && HexColor.IsMatch(x.line)))
            .Select(x => $"{Relative(x.file)}:{x.index + 1}: {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Farben gehören nach design/xaml/Design.xaml und werden per {StaticResource …} referenziert:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void JederReferenzierteRessourcenschluessel_IstDefiniert()
    {
        var designKeys = XamlFiles(Path.Combine(RepoRoot, "design", "xaml")).SelectMany(DefinedKeys).ToHashSet();
        var missing = new List<string>();

        foreach (var (project, scope) in ResourceScopes)
        {
            var defined = new HashSet<string>(designKeys);
            foreach (var reachable in scope)
            {
                defined.UnionWith(XamlFiles(ProjectDir(reachable)).SelectMany(DefinedKeys));
            }

            foreach (var file in Directory.EnumerateFiles(ProjectDir(project), "*.*", SearchOption.AllDirectories)
                         .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !IsBuildOutput(f)))
            {
                var text = File.ReadAllText(file);
                var regex = file.EndsWith(".xaml") ? XamlReference : CodeReference;
                foreach (Match match in regex.Matches(text))
                {
                    var key = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (!defined.Contains(key))
                    {
                        missing.Add($"{Relative(file)}: {key}");
                    }
                }
            }
        }

        Assert.True(missing.Count == 0, "Nicht definierte Ressourcenschlüssel:\n" + string.Join("\n", missing.Distinct()));
    }

    [Fact]
    public void JedeDateiImIconVerzeichnis_StehtImKatalog()
    {
        var catalog = File.ReadAllText(Path.Combine(RepoRoot, "design", "KATALOG.md"));
        var iconDir = Path.Combine(RepoRoot, "design", "icons");
        var missing = Directory.EnumerateFiles(iconDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("LICENSE", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(Path.Combine(RepoRoot, "design"), f).Replace('\\', '/'))
            .Where(rel => !catalog.Contains(rel))
            .ToList();

        Assert.True(missing.Count == 0, "Nicht in design/KATALOG.md aufgeführt:\n" + string.Join("\n", missing));
    }

    private static IEnumerable<string> DefinedKeys(string xamlFile) =>
        KeyDefinition.Matches(File.ReadAllText(xamlFile)).Select(m => m.Groups[1].Value);

    private static IEnumerable<string> XamlFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*.xaml", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f));

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("bin") || parts.Contains("obj");
    }

    private static string ProjectDir(string project) => Path.Combine(RepoRoot, "src", project);

    private static string Relative(string path) => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HaelpMi.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("HaelpMi.sln nicht gefunden - Test muss im Repo-Checkout laufen.");
    }
}
