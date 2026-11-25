using System.Security.AccessControl;
using System.Security.Principal;
using HaelpMi.Core.Autostart;
using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.E2E.Tests;

/// <summary>T1: stille Installation und Prüfung des Ergebnisses, gemeinsam für A (Admin), B und C (User).</summary>
internal static class Installation
{
    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(10);

    public static async Task InstalliereUndPruefeAsync(Role rolle, string geraet)
    {
        var installer = E2EUmgebung.InstallerPfad;
        Assert.True(File.Exists(installer), $"Installer fehlt: {installer}");
        Assert.False(File.Exists(AppPaths.SettingsFilePath),
            "settings.json existiert schon - VM ist nicht auf clean-base, eine Erstinstallation lässt sich so nicht prüfen.");

        HaelpMiApp.BeendeAlles();
        var gruppeAusLizenz = E2EUmgebung.LizenzPfad is { } lizenzDatei
            ? JsonFileStore.Load<License>(lizenzDatei)?.CustomerGroupId
            : null;
        var vorgabe = SchreibeVorabEinstellungen(rolle, gruppeAusLizenz ?? Guid.Empty);
        if (E2EUmgebung.LizenzPfad is { } lizenz)
        {
            // Wie beim Kunden: lizenz.json liegt vor dem ersten Agent-Start bereit (LicenseReader).
            File.Copy(lizenz, AppPaths.LicenseFilePath, overwrite: true);
        }

        Directory.CreateDirectory(Artefakte.Ordner);
        var log = Path.Combine(Artefakte.Ordner, $"installer-{geraet}.log");
        var exitCode = await Prozess.UeberShellStartenUndWartenAsync(
            installer, $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"{log}\"", InstallerTimeout);
        Assert.True(exitCode == 0, $"Installer endete mit Exit-Code {exitCode}, Log: {log}");

        // FR-34: Machine-Scope unter Program Files und ProgramData.
        Assert.True(Directory.Exists(E2EUmgebung.ProgrammOrdner), $"{E2EUmgebung.ProgrammOrdner} fehlt.");
        foreach (var datei in new[] { HaelpMiApp.AgentProzess + ".exe", HaelpMiApp.KonfigProzess + ".exe", "deployment.json", "installed-by.json" })
        {
            Assert.True(File.Exists(Path.Combine(E2EUmgebung.ProgrammOrdner, datei)), $"{datei} fehlt im Programme-Ordner.");
        }

        Assert.True(Directory.Exists(E2EUmgebung.DatenOrdner), $"{E2EUmgebung.DatenOrdner} fehlt.");

        // FR-35: Installer-Typ bestimmt die Rolle.
        var deployment = JsonFileStore.Load<DeploymentInfo>(Path.Combine(E2EUmgebung.ProgrammOrdner, "deployment.json"));
        Assert.NotNull(deployment);
        Assert.Equal(rolle, deployment!.Role);

        // MessageValidation.IsPlausible verwirft jede Nachricht mit Null-GUID, ein solcher Installer kann nie kommunizieren.
        Assert.NotEqual(Guid.Empty, deployment.CustomerGroupId);
        if (gruppeAusLizenz is { } gruppe)
        {
            Assert.Equal(gruppe, deployment.CustomerGroupId);
        }

        // FR-3/FR-37: vorab geschriebene settings.json bleibt unverändert, DeviceId inklusive.
        var settings = JsonFileStore.Load<OwnSettings>(AppPaths.SettingsFilePath);
        Assert.NotNull(settings);
        Assert.NotEqual(Guid.Empty, settings!.DeviceId);
        Assert.Equal(vorgabe.DeviceId, settings.DeviceId);
        Assert.Equal(vorgabe.RoomName, settings.RoomName);
        Assert.Equal(vorgabe.RoomNumber, settings.RoomNumber);

        // FR-4
        Assert.True(File.Exists(HaelpMiApp.VerknuepfungFuer(rolle)), $"Startmenü-Verknüpfung fehlt: {HaelpMiApp.VerknuepfungFuer(rolle)}");

        // NFR-2: Autostart über die geplante Aufgabe.
        var aufgabe = Prozess.Ausfuehren("schtasks.exe", $"/Query /TN \"{AutostartRegistrar.TaskName}\"");
        Assert.True(aufgabe.ExitCode == 0, $"Geplante Aufgabe '{AutostartRegistrar.TaskName}' fehlt: {aufgabe.Ausgabe}");

        // Der Update-Dienst wird auf diesem Stand absichtlich nicht angelegt ([Run] in HaelpMiCommon.iss.inc).
        var dienst = Prozess.Ausfuehren("sc.exe", "query HaelpMiUpdateService");
        Assert.True(dienst.ExitCode != 0, "HaelpMiUpdateService ist registriert, laut Installer-Skript sollte es ihn nicht geben.");

        // Die postinstall-Einträge starten Agent und Konfiguration mit dem Token des Installers, je nach Aufrufer
        // erhöht oder in einer fremden Sitzung. Der Test braucht den Agent rechtelos in der eigenen Sitzung, wie nach einer Anmeldung.
        await Task.Delay(TimeSpan.FromSeconds(3));
        HaelpMiApp.BeendeAlles();
        await HaelpMiApp.StarteAgentAsync();
        Rendezvous.Signal($"{geraet.ToLowerInvariant()}-installiert", settings.DeviceId.ToString());

        // Nach dem Signal, damit eine fehlende Regel nur T1 rot macht und die Gegenseite nicht ins Zeitlimit läuft.
        // Ohne eigene Programmregel zeigt Windows beim ersten Lauschen den Firewall-Dialog, der das Alarmfenster verdeckt;
        // das Dashboard (HaelpMi.Config.exe) öffnet nur in der Admin-Rolle Sockets.
        var agentExe = Path.Combine(E2EUmgebung.ProgrammOrdner, HaelpMiApp.AgentProzess + ".exe");
        PruefeProgrammregel("HälpMi Agent", agentExe);
        var regelProgramme = new List<string> { HaelpMiApp.AgentProzess + ".exe" };
        if (rolle == Role.Admin)
        {
            PruefeProgrammregel("HälpMi Konfiguration", HaelpMiApp.KonfigExe);
            regelProgramme.Add(HaelpMiApp.KonfigProzess + ".exe");
        }

        PruefeRegelnVorErstemStart(log, regelProgramme);
        PruefeKeineBlockregel(agentExe, HaelpMiApp.KonfigExe);
        PruefeBetriebsLog();
    }

    /// <summary>Betriebs-Log des Agents (AGENTS.md Teil A): agent.log neben crash.log, eine JSON-Zeile je Eintrag, Start als INFO.</summary>
    private static void PruefeBetriebsLog()
    {
        // Die jüngste Datei: der Agent schreibt nach Z:, wenn erreichbar, sonst lokal; eine ältere Kopie am anderen Ort zählt nicht.
        var datei = E2EUmgebung.LogOrdner().Select(o => new FileInfo(Path.Combine(o, "agent.log")))
            .Where(f => f.Exists).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
        Assert.True(datei is not null, $"agent.log fehlt in {string.Join(" und ", E2EUmgebung.LogOrdner())}.");

        using var strom = new FileStream(datei!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var leser = new StreamReader(strom);
        var zeilen = leser.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(zeilen);
        var stufen = new[] { "FEHLER", "WARNUNG", "INFO", "DEBUG" };
        foreach (var zeile in zeilen)
        {
            using var json = System.Text.Json.JsonDocument.Parse(zeile);
            var wurzel = json.RootElement;
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}[+-]\d{2}:\d{2}$", wurzel.GetProperty("zeit").GetString());
            Assert.Contains(wurzel.GetProperty("stufe").GetString(), stufen);
            Assert.Equal("agent", wurzel.GetProperty("prozess").GetString());
            Assert.False(string.IsNullOrEmpty(wurzel.GetProperty("modul").GetString()), $"Eintrag ohne Modul: {zeile}");
            Assert.False(string.IsNullOrEmpty(wurzel.GetProperty("meldung").GetString()), $"Eintrag ohne Meldung: {zeile}");
        }

        Assert.Contains(zeilen, z => z.Contains("\"stufe\":\"INFO\"", StringComparison.Ordinal) && z.Contains("gestartet", StringComparison.Ordinal));
        Assert.DoesNotContain(zeilen, z => z.Contains("\"stufe\":\"DEBUG\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// T24: Legt vor einer Reparatur-Installation eine lokale Block-Regel für den Agent an, wie Windows sie nach einer
    /// abgebrochenen Firewall-Abfrage hinterlässt, und eine Kontrollregel für eine gleichnamige .exe außerhalb des
    /// Programmordners. Der Installer muss nur die erste entfernen und das im Setup-Log festhalten.
    /// </summary>
    public static async Task RepariereMitBlockregelAsync(Role rolle, string geraet)
    {
        const string Ziel = "HaelpMi E2E T24 Block Agent";
        const string Kontrolle = "HaelpMi E2E T24 Block fremd";
        var agentExe = Path.Combine(E2EUmgebung.ProgrammOrdner, HaelpMiApp.AgentProzess + ".exe");
        var fremdeExe = Path.Combine(E2EUmgebung.Wurzel, "fremd", HaelpMiApp.AgentProzess + ".exe");

        Assert.True(File.Exists(agentExe), $"{agentExe} fehlt - T24 braucht die Installation aus T1 (Filter z. B. Bereich=Basis|Bereich=R222).");

        try
        {
            Prozess.PowerShell(
                $"New-NetFirewallRule -DisplayName '{Ziel}' -Direction Inbound -Action Block -Profile Any -Program '{agentExe}' | Out-Null; " +
                $"New-NetFirewallRule -DisplayName '{Kontrolle}' -Direction Inbound -Action Block -Profile Any -Program '{fremdeExe}' | Out-Null; 'ok'");
            Assert.Contains(Ziel, BlockRegelnFuer(agentExe));

            HaelpMiApp.BeendeAlles();
            Directory.CreateDirectory(Artefakte.Ordner);
            var log = Path.Combine(Artefakte.Ordner, $"installer-{geraet}-reparatur.log");
            var exitCode = await Prozess.UeberShellStartenUndWartenAsync(
                E2EUmgebung.InstallerPfad, $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"{log}\"", InstallerTimeout);
            Assert.True(exitCode == 0, $"Reparatur-Installation endete mit Exit-Code {exitCode}, Log: {log}");

            PruefeKeineBlockregel(agentExe, HaelpMiApp.KonfigExe);
            PruefeProgrammregel("HälpMi Agent", agentExe);
            if (rolle == Role.Admin)
            {
                PruefeProgrammregel("HälpMi Konfiguration", HaelpMiApp.KonfigExe);
            }

            Assert.True(BlockRegelnFuer(fremdeExe).Contains(Kontrolle, StringComparison.Ordinal),
                $"Der Installer hat die Block-Regel für {fremdeExe} außerhalb des Programmordners entfernt (Filter zu breit), Log: {log}");

            var zeilen = File.ReadAllLines(log);
            Assert.True(zeilen.Any(z => z.Contains("Block-Regel", StringComparison.Ordinal) && z.Contains("entfernt: " + Ziel, StringComparison.Ordinal)),
                $"Setup-Log {log} enthält keine Zeile „Block-Regel … entfernt: {Ziel}“.");
            Assert.DoesNotContain(zeilen, z => z.Contains(Kontrolle, StringComparison.Ordinal));
        }
        finally
        {
            Prozess.PowerShell(
                $"Remove-NetFirewallRule -DisplayName '{Ziel}' -ErrorAction SilentlyContinue; " +
                $"Remove-NetFirewallRule -DisplayName '{Kontrolle}' -ErrorAction SilentlyContinue; 'ok'");
            // Wie in T1: der vom Installer gestartete Agent läuft mit dessen Token, der Test braucht ihn rechtelos.
            await Task.Delay(TimeSpan.FromSeconds(3));
            HaelpMiApp.BeendeAlles();
            await HaelpMiApp.StarteAgentAsync();
        }
    }

    /// <summary>
    /// Startet ein HälpMi-Prozess vor seiner Allow-Regel und lauscht, fragt Windows nach; eine abgebrochene
    /// Abfrage legt Inbound-Block-Regeln an, die die Allow-Regel schlagen (Gerät empfängt keinen Alarm).
    /// </summary>
    /// <param name="programme">Dateinamen der Programme, deren netsh-„add rule“ vor dem ersten [Run]-Start stehen muss.</param>
    private static void PruefeRegelnVorErstemStart(string setupLog, IReadOnlyList<string> programme)
    {
        var zeilen = File.ReadAllLines(setupLog);
        var ersterStart = Array.FindIndex(zeilen, z =>
            z.Contains("Filename:", StringComparison.Ordinal)
            && (z.Contains(HaelpMiApp.AgentProzess + ".exe", StringComparison.OrdinalIgnoreCase)
                || z.Contains(HaelpMiApp.KonfigProzess + ".exe", StringComparison.OrdinalIgnoreCase)));
        Assert.True(ersterStart >= 0, $"Setup-Log {setupLog} enthält keinen [Run]-Start von Agent oder Konfiguration.");

        foreach (var programm in programme)
        {
            var regel = Array.FindIndex(zeilen, z =>
                z.Contains("netsh advfirewall firewall add rule", StringComparison.Ordinal)
                && z.Contains("\\" + programm + "\"", StringComparison.OrdinalIgnoreCase)
                && z.Contains("Rückgabecode 0", StringComparison.Ordinal));
            Assert.True(regel >= 0, $"Setup-Log {setupLog} enthält kein erfolgreiches netsh „add rule“ für {programm}.");
            Assert.True(regel < ersterStart,
                $"Firewall-Regel für {programm} entstand erst in Zeile {regel + 1}, der erste HälpMi-Start steht schon in " +
                $"Zeile {ersterStart + 1}: {zeilen[ersterStart].Trim()}");
        }
    }

    public static void PruefeKeineBlockregel(params string[] programme)
    {
        var ausgabe = BlockRegelnFuer(programme);
        Assert.True(string.IsNullOrWhiteSpace(ausgabe),
            $"Inbound-Block-Regeln für HälpMi-Programme vorhanden (Windows-Abfrage abgebrochen?):\n{ausgabe}");
    }

    /// <summary>Inbound-Block-Regeln aller Speicher (auch GPO) mit genau einem der Programme, je Zeile „Name|Profil|Programm“.</summary>
    public static string BlockRegelnFuer(params string[] programme)
    {
        // Ohne -Direction/-Action: Get-NetFirewallRule meldet bei null Treffern einen Fehler, PowerShell endet dann mit 1.
        var liste = string.Join(",", programme.Select(p => $"'{p}'"));
        return Prozess.PowerShell(
            $"$p = @({liste}); " +
            "Get-NetFirewallRule -PolicyStore ActiveStore | Where-Object { $_.Direction -eq 'Inbound' -and $_.Action -eq 'Block' } | ForEach-Object { " +
            "$a = $_ | Get-NetFirewallApplicationFilter; " +
            "if ($p -contains [Environment]::ExpandEnvironmentVariables([string]$a.Program)) { '{0}|{1}|{2}' -f $_.DisplayName, $_.Profile, $a.Program } }");
    }

    /// <summary>
    /// Die Installer brechen eine stille Erstinstallation ohne settings.json ab (FR-37, InitializeSetup in
    /// HaelpMiCommon.iss.inc). Liegt sie schon vor, überspringen sie die Raum-Seite und schreiben sie nicht
    /// neu (ShouldSkipPage, CurStepChanged) - so wird die stille Installation möglich.
    /// </summary>
    /// <param name="gruppe">CustomerGroupId des Laufs aus lizenz.json; nur Cache wie in WriteSettingsJson, zur Laufzeit gilt deployment.json.</param>
    private static OwnSettings SchreibeVorabEinstellungen(Role rolle, Guid gruppe)
    {
        var ordner = new DirectoryInfo(E2EUmgebung.DatenOrdner);
        ordner.Create();

        // Wie [Dirs] users-modify im Installer: der rechtelose Agent muss hier schreiben können,
        // unabhängig davon, ob Inno die Rechte auf einen schon vorhandenen Ordner nochmals setzt.
        var acl = ordner.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        ordner.SetAccessControl(acl);

        // Felder wie in WriteSettingsJson (HaelpMiCommon.iss.inc).
        var settings = new OwnSettings
        {
            DeviceId = Guid.NewGuid(),
            ComputerName = Environment.MachineName,
            RoomName = E2EUmgebung.Raum,
            RoomNumber = E2EUmgebung.Raumnummer,
            Role = rolle,
            CustomerGroupId = gruppe,
            AppliedConfigVersion = 0,
            FirstRunCompleted = true,
        };
        JsonFileStore.Save(AppPaths.SettingsFilePath, settings);
        return settings;
    }

    public static void PruefeProgrammregel(string name, string programm)
    {
        var ausgabe = Prozess.PowerShell(
            $"$r = @(Get-NetFirewallRule -DisplayName '{name}' -ErrorAction SilentlyContinue); " +
            "if ($r.Count -ne 1) { 'Anzahl Regeln: ' + $r.Count; return }; " +
            "$a = $r[0] | Get-NetFirewallApplicationFilter; " +
            "'{0}|{1}|{2}|{3}|{4}' -f $r[0].Enabled, $r[0].Direction, $r[0].Action, $r[0].Profile, $a.Program");
        Assert.Equal($"True|Inbound|Allow|Any|{programm}", ausgabe, ignoreCase: true);
    }
}
