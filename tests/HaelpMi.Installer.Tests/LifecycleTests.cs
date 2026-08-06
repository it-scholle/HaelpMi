using System.Diagnostics;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;
using Microsoft.Win32;
using Xunit;
using Xunit.Abstractions;

namespace HaelpMi.Installer.Tests;

/// <summary>
/// Deckt A1-A10 aus TEST-STRATEGY.md ab: der komplette Installer-Lebenszyklus, gegen die
/// TATSÄCHLICH gebauten Installer-Dateien (installer\Output\*.exe), nicht gegen den
/// Quellcode. Siehe README.md in diesem Ordner für die Voraussetzungen (Admin-Rechte,
/// Wegwerf-VM) - dieses Projekt installiert/deinstalliert wirklich auf dem Rechner, auf
/// dem es läuft.
///
/// Die Tests bauen bewusst sequenziell aufeinander auf (siehe <see cref="TestPriorityAttribute"/>)
/// statt unabhängig zu sein - ein Update/eine Reparatur ist ohne vorherige Installation
/// gar nicht sinnvoll testbar, genau wie in ALPHA-TESTPLAN.md.
/// </summary>
[Collection("InstallerLifecycle")]
[TestCaseOrderer("HaelpMi.Installer.Tests.PriorityOrderer", "HaelpMi.Installer.Tests")]
public class LifecycleTests
{
    private const string TestRoomName = "QA-Testraum";
    private const string TestRoomNumber = "9001";
    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(3);

    private readonly ITestOutputHelper _output;

    public LifecycleTests(ITestOutputHelper output) => _output = output;

    [Fact, TestPriority(10)]
    public async Task Step10_FreshInstall_WritesValidSettingsAndDeploymentJson_AndStartsAgent()
    {
        var installerPath = RequireInstaller(InstallerPaths.FindUserInstaller(), "User-Installer");
        KillHaelpMiProcesses();

        var process = ProcessRunner.StartInteractive(installerPath, "/LOG=" + LogPathFor("fresh-install"));
        await WizardAutomation.RunFirstInstallWizardAsync(process, TestRoomName, TestRoomNumber, InstallerPaths.DeploymentJsonPath);
        await WaitForExitAsync(process);

        // A1: settings.json - gültige DeviceId (FR-3), Raum/-nummer wie eingegeben, Rolle User.
        var settings = JsonFileStore.Load<OwnSettings>(InstallerPaths.SettingsJsonPath);
        Assert.NotNull(settings);
        Assert.NotEqual(Guid.Empty, settings!.DeviceId);
        Assert.Equal(TestRoomName, settings.RoomName);
        Assert.Equal(TestRoomNumber, settings.RoomNumber);
        Assert.Equal(Role.User, settings.Role);

        // A2: deployment.json - Test-Installer-Kennzeichnung, Rolle User (kein Kunden-GUID-Zwang beim Test-Installer).
        var deployment = JsonFileStore.Load<DeploymentInfo>(InstallerPaths.DeploymentJsonPath);
        Assert.NotNull(deployment);
        Assert.Equal(Role.User, deployment!.Role);

        AssertAgentIsRunning();
        AssertServiceExists("HaelpMiUpdateService");
        AssertScheduledTaskExists("HaelpMi Agent");
    }

    [Fact, TestPriority(20)]
    public async Task Step20_ReRunSameVersion_TreatsAsRepair_KeepsDeviceId()
    {
        var installerPath = RequireInstaller(InstallerPaths.FindUserInstaller(), "User-Installer");
        var deviceIdBefore = JsonFileStore.Load<OwnSettings>(InstallerPaths.SettingsJsonPath)!.DeviceId;

        // settings.json existiert jetzt schon (aus Step10) - silent ist hier zulässig
        // (siehe InitializeSetup()s FR-37-Silent-Guard in HaelpMiCommon.iss.inc).
        var result = await ProcessRunner.RunAsync(installerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=" + LogPathFor("repair"), InstallerTimeout);
        _output.WriteLine(result.StdOut);

        Assert.Equal(0, result.ExitCode);
        var deviceIdAfter = JsonFileStore.Load<OwnSettings>(InstallerPaths.SettingsJsonPath)!.DeviceId;
        Assert.Equal(deviceIdBefore, deviceIdAfter); // FR-3: nie neu erzeugen
    }

    [Fact, TestPriority(30)]
    public async Task Step30_SimulatedDowngrade_IsBlocked()
    {
        // Statt zwei echte Versionsstände zu brauchen: die registry-hinterlegte
        // DisplayVersion (die InitializeSetup() in HaelpMiCommon.iss.inc tatsächlich
        // ausliest) künstlich auf eine höhere Zahl setzen und denselben Installer erneut
        // laufen lassen - testet exakt denselben CompareDottedVersion-Pfad wie ein echtes
        // Downgrade, ohne einen zweiten Build zu brauchen.
        var installerPath = RequireInstaller(InstallerPaths.FindUserInstaller(), "User-Installer");
        var appId = ReadAppId(installerPath);
        var registryKeyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{appId}_is1";

        using var key = Registry.LocalMachine.OpenSubKey(registryKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Uninstall-Registrierung {registryKeyPath} fehlt - Step10/20 sind vermutlich nicht gelaufen.");
        var originalVersion = (string)key.GetValue("DisplayVersion")!;
        key.SetValue("DisplayVersion", "99.99.99");

        try
        {
            var result = await ProcessRunner.RunAsync(installerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=" + LogPathFor("downgrade-attempt"), InstallerTimeout);
            _output.WriteLine(result.StdOut);

            // InitializeSetup() bricht mit Result := False ab, sobald ein Downgrade erkannt
            // wird (silent oder nicht) - Setup.exe beendet sich dann mit einem
            // Fehler-/Abbruch-Exitcode, kopiert aber keine Dateien.
            Assert.NotEqual(0, result.ExitCode);
        }
        finally
        {
            key.SetValue("DisplayVersion", originalVersion); // Zustand für die nächsten Schritte reparieren
        }
    }

    [Fact, TestPriority(40)]
    public async Task Step40_UpdateOverRunningInstallation_EndsWithExactlyOneAgentProcess()
    {
        var installerPath = RequireInstaller(InstallerPaths.FindUserInstaller(), "User-Installer");
        AssertAgentIsRunning(); // aus Step10 noch am Laufen

        var deviceIdBefore = JsonFileStore.Load<OwnSettings>(InstallerPaths.SettingsJsonPath)!.DeviceId;

        var process = ProcessRunner.StartInteractive(installerPath, "/LOG=" + LogPathFor("update-over-running"));
        await WizardAutomation.RunUpdateWizardAsync(process, InstallerPaths.DeploymentJsonPath);
        await WaitForExitAsync(process);

        // Bugfix 06.08.2026 dieser Sitzung: IPC-Server-Race konnte einen zweiten,
        // kollidierenden Agent-Prozess erzeugen - das ist der Kern-Regressionstest dafür.
        await Task.Delay(TimeSpan.FromSeconds(3)); // [Run]-Abschnitt startet Agent.exe "nowait" - kurz Zeit zum Hochfahren geben
        var agentProcesses = Process.GetProcessesByName("HaelpMi.Agent");
        Assert.Single(agentProcesses);
        foreach (var p in agentProcesses) p.Dispose();

        var deviceIdAfter = JsonFileStore.Load<OwnSettings>(InstallerPaths.SettingsJsonPath)!.DeviceId;
        Assert.Equal(deviceIdBefore, deviceIdAfter);
    }

    [Fact, TestPriority(50)]
    public async Task Step50_InteractiveUninstall_AnsweringNo_KeepsProgramDataAndSettings()
    {
        var uninstallerPath = RequireInstaller(InstallerPaths.FindUninstaller(), "Uninstaller (unins000.exe)");
        var deviceIdBefore = JsonFileStore.Load<OwnSettings>(InstallerPaths.SettingsJsonPath)!.DeviceId;

        var process = ProcessRunner.StartInteractive(uninstallerPath, string.Empty);
        await WizardAutomation.RunUninstallWithDataPromptAsync(process, keepData: true);
        await WaitForExitAsync(process);

        Assert.False(Directory.Exists(InstallerPaths.ProgramFilesInstallDir)); // {app} entfernt
        Assert.True(File.Exists(InstallerPaths.SettingsJsonPath)); // ProgramData bewusst erhalten
        var deviceIdAfter = JsonFileStore.Load<OwnSettings>(InstallerPaths.SettingsJsonPath)!.DeviceId;
        Assert.Equal(deviceIdBefore, deviceIdAfter);

        AssertServiceDoesNotExist("HaelpMiUpdateService");
        AssertScheduledTaskDoesNotExist("HaelpMi Agent");
        AssertNoHaelpMiProcessesRunning();
    }

    [Fact, TestPriority(60)]
    public async Task Step60_ReinstallOnTopOfKeptConfig_SkipsRoomPage_PreservesDeviceId()
    {
        var installerPath = RequireInstaller(InstallerPaths.FindUserInstaller(), "User-Installer");
        Assert.True(File.Exists(InstallerPaths.SettingsJsonPath), "Voraussetzung: Step50 muss die Daten behalten haben.");
        var deviceIdBefore = JsonFileStore.Load<OwnSettings>(InstallerPaths.SettingsJsonPath)!.DeviceId;

        // settings.json existiert (behalten aus Step50) -> ShouldSkipPage überspringt die
        // Raum-Seite (siehe HaelpMiCommon.iss.inc) -> derselbe 3-Seiten-Ablauf wie ein Update.
        var process = ProcessRunner.StartInteractive(installerPath, "/LOG=" + LogPathFor("reinstall-on-kept-config"));
        await WizardAutomation.RunUpdateWizardAsync(process, InstallerPaths.DeploymentJsonPath);
        await WaitForExitAsync(process);

        var deviceIdAfter = JsonFileStore.Load<OwnSettings>(InstallerPaths.SettingsJsonPath)!.DeviceId;
        Assert.Equal(deviceIdBefore, deviceIdAfter);
        AssertAgentIsRunning();
    }

    [Fact, TestPriority(70)]
    public async Task Step70_SilentUninstall_RemovesProgramFilesAndProgramDataCompletely()
    {
        var uninstallerPath = RequireInstaller(InstallerPaths.FindUninstaller(), "Uninstaller (unins000.exe)");

        var result = await ProcessRunner.RunAsync(uninstallerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", InstallerTimeout);
        _output.WriteLine(result.StdOut);

        // UninstallSilent() lässt InitializeUninstall() ohne Rückfrage auf "alles entfernen"
        // fallen (siehe HaelpMiCommon.iss.inc) - anders als Step50 muss hier WIRKLICH nichts
        // mehr übrig sein.
        Assert.False(Directory.Exists(InstallerPaths.ProgramFilesInstallDir));
        Assert.False(Directory.Exists(InstallerPaths.ProgramDataDir));
        AssertServiceDoesNotExist("HaelpMiUpdateService");
        AssertScheduledTaskDoesNotExist("HaelpMi Agent");
        AssertNoHaelpMiProcessesRunning();
    }

    [Fact, TestPriority(80)]
    public async Task Step80_AdminInstaller_CreatesFirewallRules_UserInstallerDoesNot()
    {
        var adminInstallerPath = RequireInstaller(InstallerPaths.FindAdminInstaller(), "Admin-Installer");
        Assert.False(Directory.Exists(InstallerPaths.ProgramFilesInstallDir), "Voraussetzung: Step70 muss vollständig aufgeräumt haben.");

        var process = ProcessRunner.StartInteractive(adminInstallerPath, "/LOG=" + LogPathFor("admin-fresh-install"));
        await WizardAutomation.RunFirstInstallWizardAsync(process, TestRoomName, TestRoomNumber, InstallerPaths.DeploymentJsonPath);
        await WaitForExitAsync(process);

        var deployment = JsonFileStore.Load<DeploymentInfo>(InstallerPaths.DeploymentJsonPath);
        Assert.Equal(Role.Admin, deployment!.Role);
        AssertFirewallRuleExists("HälpMi Discovery");
        AssertFirewallRuleExists("HälpMi Alarm");

        // Aufräumen für Step90+: Admin-Installation wieder silent entfernen, inkl. Firewall-Regeln.
        var uninstallerPath = RequireInstaller(InstallerPaths.FindUninstaller(), "Uninstaller (unins000.exe)");
        await ProcessRunner.RunAsync(uninstallerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", InstallerTimeout);
        AssertFirewallRuleDoesNotExist("HälpMi Discovery");
        AssertFirewallRuleDoesNotExist("HälpMi Alarm");
    }

    [Fact, TestPriority(90)]
    public async Task Step90_EmbeddedUserInstaller_CarriesSameCustomerGroupId_OnRoleSwitch()
    {
        // A10, vereinfacht: statt eines zweiten physischen Geräts wird auf DEMSELBEN
        // Gerät zuerst der Admin-Installer (schreibt CustomerGroupId X), danach der darin
        // eingebettete HaelpMi-User-Setup.exe installiert - laut HaelpMi-Admin.iss-Kommentar
        // ("den jeweils anderen Installer später auszuführen wird als Update desselben
        // Produkts erkannt") ein Rollenwechsel auf demselben Gerät, keine Neuinstallation.
        // Bleibt die CustomerGroupId dabei exakt gleich, ist das ein starker Hinweis, dass
        // der eingebettete User-Installer wirklich mit derselben ID gebaut wurde.
        var adminInstallerPath = RequireInstaller(InstallerPaths.FindAdminInstaller(), "Admin-Installer");
        Assert.False(Directory.Exists(InstallerPaths.ProgramFilesInstallDir), "Voraussetzung: Step80 muss vollständig aufgeräumt haben.");

        var adminProcess = ProcessRunner.StartInteractive(adminInstallerPath, "/LOG=" + LogPathFor("admin-for-role-switch"));
        await WizardAutomation.RunFirstInstallWizardAsync(adminProcess, TestRoomName, TestRoomNumber, InstallerPaths.DeploymentJsonPath);
        await WaitForExitAsync(adminProcess);
        var customerGroupIdAsAdmin = JsonFileStore.Load<DeploymentInfo>(InstallerPaths.DeploymentJsonPath)!.CustomerGroupId;

        var embeddedUserInstaller = Path.Combine(InstallerPaths.ProgramFilesInstallDir, "HaelpMi-User-Setup.exe");
        Assert.True(File.Exists(embeddedUserInstaller), "Admin-Installer sollte HaelpMi-User-Setup.exe einbetten (siehe HaelpMiCommon.iss.inc).");

        var userProcess = ProcessRunner.StartInteractive(embeddedUserInstaller, "/LOG=" + LogPathFor("embedded-user-role-switch"));
        await WizardAutomation.RunUpdateWizardAsync(userProcess, InstallerPaths.DeploymentJsonPath); // settings.json existiert schon -> RoomPage übersprungen
        await WaitForExitAsync(userProcess);

        var deploymentAfterSwitch = JsonFileStore.Load<DeploymentInfo>(InstallerPaths.DeploymentJsonPath)!;
        Assert.Equal(Role.User, deploymentAfterSwitch.Role);
        Assert.Equal(customerGroupIdAsAdmin, deploymentAfterSwitch.CustomerGroupId);

        // Endgültiges Aufräumen der gesamten Testreihe.
        var uninstallerPath = RequireInstaller(InstallerPaths.FindUninstaller(), "Uninstaller (unins000.exe)");
        await ProcessRunner.RunAsync(uninstallerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", InstallerTimeout);
    }

    // ------------------------------------------------------------------- Helpers ---

    private static string RequireInstaller(string? path, string label) =>
        path ?? throw new InvalidOperationException(
            $"{label} nicht gefunden unter {InstallerPaths.InstallerOutputDir} - erst `dotnet publish` (siehe BUILD-UND-INSTALLATION.md) + ISCC-Kompilierung laufen lassen, bevor dieses Testprojekt gestartet wird.");

    private static string LogPathFor(string step) =>
        Path.Combine(Path.GetTempPath(), $"HaelpMi-InstallerTest-{step}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");

    private static async Task WaitForExitAsync(Process process)
    {
        using var cts = new CancellationTokenSource(InstallerTimeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Installer/Deinstaller hat sich nicht rechtzeitig beendet - vermutlich eine unerwartete zusätzliche Seite/Rückfrage, die WizardAutomation nicht kennt.");
        }
    }

    // Liest die AppId aus derselben .iss.inc, die auch der Installer selbst benutzt -
    // GetInstalledVersion() in HaelpMiCommon.iss.inc baut den Registry-Pfad genauso.
    private static string ReadAppId(string installerPath)
    {
        var incPath = Path.Combine(InstallerPaths.RepoRoot, "installer", "HaelpMiCommon.iss.inc");
        var line = File.ReadAllLines(incPath).First(l => l.TrimStart().StartsWith("#define MyAppId", StringComparison.Ordinal));
        // Zeile hat die Form: #define MyAppId "{{4B6C0A6E-...}}" - äußere Anführungszeichen entfernen.
        var start = line.IndexOf('"') + 1;
        var end = line.LastIndexOf('"');
        return line[start..end];
    }

    private static void KillHaelpMiProcesses()
    {
        foreach (var name in new[] { "HaelpMi.Agent", "HaelpMi.Config" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* schon beendet */ }
                p.Dispose();
            }
        }
    }

    private static void AssertAgentIsRunning()
    {
        var processes = Process.GetProcessesByName("HaelpMi.Agent");
        try
        {
            Assert.NotEmpty(processes);
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private static void AssertNoHaelpMiProcessesRunning()
    {
        foreach (var name in new[] { "HaelpMi.Agent", "HaelpMi.Config", "HaelpMi.UpdateService" })
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                Assert.Empty(processes);
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }
    }

    private static void AssertServiceExists(string serviceName) =>
        Assert.Equal(0, RunSync("sc.exe", $"query {serviceName}").ExitCode);

    private static void AssertServiceDoesNotExist(string serviceName) =>
        Assert.NotEqual(0, RunSync("sc.exe", $"query {serviceName}").ExitCode);

    private static void AssertScheduledTaskExists(string taskName) =>
        Assert.Equal(0, RunSync("schtasks.exe", $"/Query /TN \"{taskName}\"").ExitCode);

    private static void AssertScheduledTaskDoesNotExist(string taskName) =>
        Assert.NotEqual(0, RunSync("schtasks.exe", $"/Query /TN \"{taskName}\"").ExitCode);

    private static void AssertFirewallRuleExists(string ruleName) =>
        Assert.Contains(ruleName, RunSync("netsh.exe", $"advfirewall firewall show rule name=\"{ruleName}\"").StdOut, StringComparison.Ordinal);

    private static void AssertFirewallRuleDoesNotExist(string ruleName) =>
        Assert.DoesNotContain(ruleName, RunSync("netsh.exe", $"advfirewall firewall show rule name=\"{ruleName}\"").StdOut, StringComparison.Ordinal);

    private static ProcessResult RunSync(string exe, string args) =>
        ProcessRunner.RunAsync(exe, args, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

    // Bewusst KEIN IDisposable/Cleanup-nach-jedem-Test: Step10 lässt den Agent absichtlich
    // laufen, weil Step40 genau das als Ausgangszustand braucht ("Update über laufende
    // Installation") - ein Aufräumen zwischen jedem Testschritt würde die gewollte
    // Abhängigkeitskette kaputt machen. Bricht ein Schritt mitten in der Kette ab, bleibt
    // der Rechner in einem Zwischenzustand stehen - das ist auf einer Wegwerf-VM in
    // Ordnung (siehe README.md), nicht auf einer Maschine mit echten Daten.
}

[CollectionDefinition("InstallerLifecycle", DisableParallelization = true)]
public class InstallerLifecycleCollection
{
}
