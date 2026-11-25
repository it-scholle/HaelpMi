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

    private static void PruefeKeineBlockregel(params string[] programme)
    {
        var liste = string.Join(",", programme.Select(p => $"'{p}'"));
        var ausgabe = Prozess.PowerShell(
            $"$p = @({liste}); " +
            "Get-NetFirewallRule -Direction Inbound -Action Block -ErrorAction SilentlyContinue | ForEach-Object { " +
            "$a = $_ | Get-NetFirewallApplicationFilter; " +
            "if ($p -contains $a.Program) { '{0}|{1}|{2}' -f $_.DisplayName, $_.Profile, $a.Program } }");
        Assert.True(string.IsNullOrWhiteSpace(ausgabe),
            $"Inbound-Block-Regeln für HälpMi-Programme vorhanden (Windows-Abfrage abgebrochen?):\n{ausgabe}");
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

    private static void PruefeProgrammregel(string name, string programm)
    {
        var ausgabe = Prozess.PowerShell(
            $"$r = @(Get-NetFirewallRule -DisplayName '{name}' -ErrorAction SilentlyContinue); " +
            "if ($r.Count -ne 1) { 'Anzahl Regeln: ' + $r.Count; return }; " +
            "$a = $r[0] | Get-NetFirewallApplicationFilter; " +
            "'{0}|{1}|{2}|{3}|{4}' -f $r[0].Enabled, $r[0].Direction, $r[0].Action, $r[0].Profile, $a.Program");
        Assert.Equal($"True|Inbound|Allow|Any|{programm}", ausgabe, ignoreCase: true);
    }
}
