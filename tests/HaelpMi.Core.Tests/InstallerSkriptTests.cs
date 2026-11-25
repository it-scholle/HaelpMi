using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Prüft die Installer-Skripte (installer/*.iss, *.iss.inc) am Quelltext. Inno Setup führt die
/// [Run]-Einträge vor CurStepChanged(ssPostInstall) aus; startet dort ein HälpMi-Prozess ohne
/// Firewall-Programmregel, fragt Windows nach, und eine abgebrochene Abfrage hinterlässt
/// Inbound-Block-Regeln, die die Allow-Regel des Installers schlagen.
/// </summary>
public class InstallerSkriptTests
{
    private static readonly string InstallerOrdner = Path.Combine(FindRepoRoot(), "installer");
    private static readonly string[] HaelpMiProgramme = ["{#MyAgentExeName}", "{#MyConfigExeName}"];

    [Fact]
    public void ErsterRunEintragMitHaelpMiProgramm_LegtVorherDieFirewallRegelnAn()
    {
        var eintraege = RunEintraege().ToList();
        Assert.NotEmpty(eintraege);

        var erster = eintraege.FirstOrDefault(e => HaelpMiProgramme.Any(p => FilenameVon(e.Zeile).Contains(p, StringComparison.Ordinal)));
        Assert.True(erster.Zeile is not null, "Kein [Run]-Eintrag startet HaelpMi.Agent.exe oder HaelpMi.Config.exe - Test an das Skript anpassen.");

        var vorher = eintraege.TakeWhile(e => e != erster)
            .Where(e => e.Zeile.Contains("HaelpMi", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(vorher.Count == 0,
            "Vor dem ersten Agent-/Config-Start steht ein weiterer HälpMi-Eintrag ohne Firewall-Regel:\n" +
            string.Join("\n", vorher.Select(e => $"{e.Datei}:{e.Nummer}: {e.Zeile}")));

        Assert.True(erster.Zeile.Contains("BeforeInstall: EnsureFirewallRules", StringComparison.Ordinal),
            $"{erster.Datei}:{erster.Nummer} startet einen HälpMi-Prozess, bevor die Firewall-Regeln angelegt sind " +
            $"(BeforeInstall: EnsureFirewallRules fehlt): {erster.Zeile}");
    }

    [Fact]
    public void FirewallRegeln_WerdenNichtErstNachDenRunEintraegenAngelegt()
    {
        var code = File.ReadAllText(Path.Combine(InstallerOrdner, "HaelpMiCommon.iss.inc"));
        var start = code.IndexOf("procedure CurStepChanged(", StringComparison.Ordinal);
        Assert.True(start >= 0, "CurStepChanged fehlt in HaelpMiCommon.iss.inc.");
        var ende = code.IndexOf("\nend;", start, StringComparison.Ordinal);
        var prozedur = code[start..ende];

        Assert.DoesNotContain("EnsureFirewallRules", prozedur);
    }

    private readonly record struct RunEintrag(string Datei, int Nummer, string Zeile);

    private static IEnumerable<RunEintrag> RunEintraege()
    {
        foreach (var datei in Directory.EnumerateFiles(InstallerOrdner).Where(IstInnoSkript).Order(StringComparer.Ordinal))
        {
            var imRun = false;
            var zeilen = File.ReadAllLines(datei);
            for (var i = 0; i < zeilen.Length; i++)
            {
                var zeile = zeilen[i].Trim();
                if (zeile.StartsWith('[') && zeile.EndsWith(']'))
                {
                    imRun = zeile.Equals("[Run]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (imRun && zeile.StartsWith("Filename:", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new RunEintrag(Path.GetFileName(datei), i + 1, zeile);
                }
            }
        }
    }

    private static string FilenameVon(string zeile)
    {
        var wert = zeile["Filename:".Length..];
        var semikolon = wert.IndexOf(';');
        return semikolon < 0 ? wert : wert[..semikolon];
    }

    private static bool IstInnoSkript(string pfad) =>
        pfad.EndsWith(".iss", StringComparison.OrdinalIgnoreCase) || pfad.EndsWith(".iss.inc", StringComparison.OrdinalIgnoreCase);

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
