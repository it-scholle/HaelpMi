using System.Text.RegularExpressions;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Prüft die Installer-Skripte (installer/*.iss, *.iss.inc) am Quelltext. Inno Setup führt die
/// [Run]-Einträge ohne postinstall vor CurStepChanged(ssPostInstall) aus; startet dort ein HälpMi-Prozess ohne
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
        Assert.True(ende > start, "Ende von CurStepChanged (Zeile \"end;\") nicht gefunden.");
        var prozedur = code[start..ende];

        Assert.DoesNotContain("EnsureFirewallRules", prozedur);
    }

    [Fact]
    public void FirewallRegeln_EntfernenZuerstBlockRegeln_OhneSchalter()
    {
        var code = InnoCode();
        Assert.DoesNotContain("BlockRegelnEntfernen", code);

        var ensure = Prozedur(code, "EnsureFirewallRules");
        var entfernen = ensure.IndexOf("EntferneBlockRegeln();", StringComparison.Ordinal);
        Assert.True(entfernen >= 0, "EnsureFirewallRules ruft EntferneBlockRegeln nicht auf.");
        var ersteRegel = ensure.IndexOf("add rule", StringComparison.Ordinal);
        Assert.True(ersteRegel < 0 || entfernen < ersteRegel, "Block-Regeln werden erst nach dem Anlegen der Allow-Regeln entfernt.");
        Assert.True(!ensure[..entfernen].Contains("#if", StringComparison.Ordinal),
            "Der Aufruf von EntferneBlockRegeln steht in einem Präprozessor-Zweig und läuft nicht in jedem Installer.");
    }

    [Fact]
    public void BlockRegeln_NurLokal_NurInboundBlock_NurDieZweiHaelpMiProgramme()
    {
        var prozedur = Prozedur(InnoCode(), "EntferneBlockRegeln");

        Assert.Contains("Get-NetFirewallRule -PolicyStore PersistentStore |", prozedur);
        Assert.Contains("$_.Direction -eq ''Inbound'' -and $_.Action -eq ''Block''", prozedur);
        Assert.Contains("$p = @(' + PsLiteral(ExpandConstant('{app}\\{#MyAgentExeName}')) + ', ' + PsLiteral(ExpandConstant('{app}\\{#MyConfigExeName}')) + '); ", prozedur);
        Assert.Contains("StringChangeEx(Result, '''', '''''', True);", Prozedur(InnoCode(), "PsLiteral", "function"));
        Assert.Single(Regex.Matches(prozedur, @"\| Remove-NetFirewallRule"));
        Assert.Contains("$_ | Remove-NetFirewallRule } } }", prozedur);
        Assert.DoesNotContain("-like", prozedur, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-match", prozedur, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Log('Block-Regel ' + Zeilen[I])", prozedur);
    }

    /// <summary>Wertet die Bedingung aus dem Installer-Skript mit PowerShell aus (nur lesend, keine Firewall-Änderung).</summary>
    [Theory]
    [InlineData(@"{app}\HaelpMi.Agent.exe", true)]
    [InlineData(@"{app}\HaelpMi.Config.exe", true)]
    [InlineData(@"{APP}\HAELPMI.AGENT.EXE", true)]
    [InlineData(@"%ProgramFiles%\HaelpMi\HaelpMi.Agent.exe", true)]
    [InlineData(@"C:\Andere\HaelpMi.Agent.exe", false)]
    [InlineData(@"{app}\Unter\HaelpMi.Agent.exe", false)]
    [InlineData(@"{app}\HaelpMi.Agent.exe.alt", false)]
    [InlineData(@"{app}\HaelpMi.UpdateService.exe", false)]
    [InlineData(@"{app}", false)]
    [InlineData("Any", false)]
    [InlineData("", false)]
    public void BlockRegel_Pfadfilter(string programm, bool entfernen)
    {
        var prozedur = Prozedur(InnoCode(), "EntferneBlockRegeln");
        const string Anfang = "if (";
        const string Ende = ") { ''entfernt: ''";
        var ende = prozedur.IndexOf(Ende, StringComparison.Ordinal);
        Assert.True(ende > 0, "Bedingung vor ''entfernt: '' nicht gefunden - Test an das Skript anpassen.");
        var anfang = prozedur.LastIndexOf(Anfang, ende, StringComparison.Ordinal) + Anfang.Length;
        var bedingung = prozedur[anfang..ende].Replace("''", "'", StringComparison.Ordinal);

        var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HaelpMi");
        var kandidat = programm.Replace("{app}", app, StringComparison.Ordinal).Replace("{APP}", app.ToUpperInvariant(), StringComparison.Ordinal);
        var skript =
            $"$p = @('{app}\\HaelpMi.Agent.exe', '{app}\\HaelpMi.Config.exe'); " +
            $"$a = [pscustomobject]@{{ Program = '{kandidat}' }}; " +
            $"if ({bedingung}) {{ 'ja' }} else {{ 'nein' }}";

        Assert.Equal(entfernen ? "ja" : "nein", PowerShell(skript));
    }

    private static string PowerShell(string skript)
    {
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(skript)))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("powershell.exe ließ sich nicht starten.");
        var fehler = p.StandardError.ReadToEndAsync();
        var ausgabe = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"PowerShell endete mit {p.ExitCode}: {fehler.Result}");
        return ausgabe.Trim();
    }

    private static string InnoCode() => File.ReadAllText(Path.Combine(InstallerOrdner, "HaelpMiCommon.iss.inc"));

    private static string Prozedur(string code, string name, string art = "procedure")
    {
        var start = code.IndexOf($"{art} {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} fehlt in HaelpMiCommon.iss.inc.");
        var ende = code.IndexOf("\nend;", start, StringComparison.Ordinal);
        Assert.True(ende > start, $"Ende von {name} (Zeile \"end;\") nicht gefunden.");
        return code[start..ende];
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
