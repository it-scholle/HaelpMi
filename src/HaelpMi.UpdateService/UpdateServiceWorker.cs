using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using HaelpMi.Core.Autostart;
using HaelpMi.Core.Ipc;
using HaelpMi.Core.Models;
using HaelpMi.Core.Updates;

namespace HaelpMi.UpdateService;

/// <summary>
/// Läuft als Windows-Dienst (LocalService/SYSTEM) - der einzige Teil von HälpMi mit
/// erhöhten Rechten, ausschließlich für Installieren/Testen/Swappen/Deinstallieren beim
/// Auto-Update (CLAUDE.md, Abschnitt 11). Der (rechtelose) Agent weist über den lokalen
/// Named Pipe an, was zu tun ist; dieser Dienst kennt selbst kein größeres Update-
/// Ablaufkonzept (kein "installiere und teste und swappe automatisch"), nur die vier
/// einzelnen, vom Agent einzeln angestoßenen Schritte - der Agent entscheidet zwischen
/// den Schritten (eigener Selbsttest, Peer-Bestätigung abwarten).
///
/// Anfragen werden bewusst sequenziell abgearbeitet (kein Fire-and-Forget pro
/// Verbindung) - ein Datei-Swap darf nie parallel zu einem zweiten laufen.
/// </summary>
public sealed class UpdateServiceWorker : BackgroundService
{
    private readonly ILogger<UpdateServiceWorker> _logger;

    // Untersuchung 09.08.2026 (Nutzerbericht "im Hintergrund läuft immer noch eine
    // Alarmmeldung", noch nicht live nachgestellt): dieses Dictionary lebt ausschließlich
    // im Arbeitsspeicher DIESES Worker-Objekts. Stirbt/startet der Dienst neu (Absturz,
    // "sc.exe stop" mitten in einem laufenden StartTest, Deinstallation während eines
    // Updates), verliert der neue Worker jede Kenntnis der zuvor gestarteten Testinstanz -
    // der Kindprozess (eine vollständige HaelpMi.Agent.exe mit eigenem
    // AlarmFlowCoordinator, siehe StartTest unten) läuft dann als Waise unbegrenzt weiter,
    // ohne dass irgendetwas ihn je wieder beendet. KillOrphanedTestInstances() räumt genau
    // das beim Dienststart auf - siehe dort.
    private readonly Dictionary<string, Process> _testProcesses = new();

    private static string AppRoot => AppContext.BaseDirectory;
    private static string VersionsRootDir => Path.Combine(AppRoot, "versions");
    private static string VersionDir(string version) => Path.Combine(VersionsRootDir, version);

    public UpdateServiceWorker(ILogger<UpdateServiceWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        KillOrphanedTestInstances();

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = CreateSecuredPipeServer();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Named Pipe konnte nicht erstellt werden.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            using (server)
            {
                try
                {
                    await server.WaitForConnectionAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Ein Dienst mit erhöhten Rechten, der wegen einer einzelnen kaputten
                    // Verbindung ganz beendet wird, macht die komplette Update-Pipeline für
                    // alle Geräte unbrauchbar - lieber loggen und die Schleife fortsetzen.
                    _logger.LogError(ex, "Fehler beim Warten auf eine Named-Pipe-Verbindung.");
                    continue;
                }

                await HandleConnectionAsync(server, stoppingToken);
            }
        }
    }

    // Der Agent läuft rechtelos im User-Kontext (CLAUDE.md), muss diesen SYSTEM/
    // LocalService-eigenen Pipe aber erreichen können - ohne diese explizite ACL würde
    // ein Standardnutzer-Prozess mit "Zugriff verweigert" abgewiesen, weil Named Pipes
    // von SYSTEM-Prozessen sonst nur für SYSTEM/Administratoren zugänglich sind.
    private static NamedPipeServerStream CreateSecuredPipeServer()
    {
        var pipeSecurity = new PipeSecurity();
        var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(authenticatedUsers, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            AppConstants.UpdateServiceIpcPipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(5)); // Install/Swap kann durch Datei-I/O etwas dauern

            using var reader = new StreamReader(server, UpdateServiceWireFormat.Encoding, leaveOpen: true);
            var line = await reader.ReadLineAsync(timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            UpdateServiceRequest? request;
            try
            {
                request = UpdateServiceWireFormat.FromJsonLine<UpdateServiceRequest>(line);
            }
            catch (Exception)
            {
                request = null;
            }

            var response = request is null
                ? new UpdateServiceResponse(false, "Ungültige Anfrage.")
                : await DispatchAsync(request, timeoutCts.Token);

            var responseBytes = UpdateServiceWireFormat.Encoding.GetBytes(UpdateServiceWireFormat.ToJsonLine(response));
            await server.WriteAsync(responseBytes, ct);
            await server.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei der Verarbeitung einer Update-Dienst-Anfrage.");
        }
    }

    private async Task<UpdateServiceResponse> DispatchAsync(UpdateServiceRequest request, CancellationToken ct)
    {
        try
        {
            return request.Command switch
            {
                UpdateServiceCommandType.Install => InstallAsync(request),
                UpdateServiceCommandType.StartTest => StartTest(request),
                UpdateServiceCommandType.ConfirmSwap => await ConfirmSwapAsync(request, ct),
                UpdateServiceCommandType.Rollback => Rollback(request),
                _ => new UpdateServiceResponse(false, "Unbekanntes Kommando."),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update-Schritt {Command} für Version {Version} fehlgeschlagen.", request.Command, request.Version);
            return new UpdateServiceResponse(false, ex.Message);
        }
    }

    private static UpdateServiceResponse InstallAsync(UpdateServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PackageZipPath) || !File.Exists(request.PackageZipPath))
        {
            return new UpdateServiceResponse(false, "Paket-Datei nicht gefunden.");
        }

        var targetDir = VersionDir(request.Version);
        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, true); // sauberer erneuter Versuch statt Datei-Mischmasch bei einem Retry
        }

        Directory.CreateDirectory(targetDir);
        ZipFile.ExtractToDirectory(request.PackageZipPath, targetDir);
        return new UpdateServiceResponse(true);
    }

    private UpdateServiceResponse StartTest(UpdateServiceRequest request)
    {
        if (request.TestPort is not { } testPort)
        {
            return new UpdateServiceResponse(false, "Kein Testport angegeben.");
        }

        var exePath = Path.Combine(VersionDir(request.Version), "HaelpMi.Agent.exe");
        if (!File.Exists(exePath))
        {
            return new UpdateServiceResponse(false, "Installierte Version nicht gefunden - zuerst Install ausführen.");
        }

        var startInfo = new ProcessStartInfo(exePath) { UseShellExecute = false };
        // Testmodus: eigene Ports, kein Autostart-Eintrag, keine echte Netzwerkteilnahme -
        // siehe HaelpMi.Agent/App.xaml.cs Kommandozeilen-Behandlung.
        startInfo.ArgumentList.Add($"--update-test-port={testPort}");

        var process = Process.Start(startInfo);
        if (process is null)
        {
            return new UpdateServiceResponse(false, "Testinstanz konnte nicht gestartet werden.");
        }

        StopTrackedTestProcess(request.Version); // falls schon eine ältere Testinstanz dieser Version lief
        _testProcesses[request.Version] = process;
        return new UpdateServiceResponse(true);
    }

    // 0-Downtime-Swap: Produktivinstanz kurz stoppen, Dateien austauschen, sofort neu
    // starten - siehe CLAUDE.md "Programm-Updates ... Port-Übernahme und Deinstallation
    // der Altversion". Die kurze Lücke zwischen Stop und Neustart ist bei einem Datei-
    // Swap eines laufenden .exe unvermeidbar, wird aber durch den vorherigen
    // Test-Port-Lauf (StartTest) minimiert - ConfirmSwap wird nur nach bereits
    // bestandenem lokalem Selbsttest + Peer-Bestätigung aufgerufen (Agent-Seite), nie blind.
    private async Task<UpdateServiceResponse> ConfirmSwapAsync(UpdateServiceRequest request, CancellationToken ct)
    {
        var newVersionDir = VersionDir(request.Version);
        if (!Directory.Exists(newVersionDir))
        {
            return new UpdateServiceResponse(false, "Getestete Version nicht gefunden - zuerst Install/StartTest ausführen.");
        }

        StopTrackedTestProcess(request.Version);
        KillProcessByName("HaelpMi.Agent");
        KillProcessByName("HaelpMi.Config");
        await Task.Delay(500, ct); // Windows gibt Datei-Handles nach Prozessende nicht immer sofort frei

        var previousDir = Path.Combine(AppRoot, "_previous");
        if (Directory.Exists(previousDir))
        {
            Directory.Delete(previousDir, true);
        }
        Directory.CreateDirectory(previousDir);

        SwapDirectoryMover.MoveAllEntries(AppRoot, previousDir, exclude: SwapDirectoryMover.BuildAppRootMoveExcludeList());
        SwapDirectoryMover.MoveAllEntries(newVersionDir, AppRoot, exclude: Array.Empty<string>());

        var newAgentPath = Path.Combine(AppRoot, "HaelpMi.Agent.exe");
        if (File.Exists(newAgentPath))
        {
            // Selbstheilungs-Erweiterung 11.08.2026 (Fehlerbericht "Autostart nicht
            // eingerichtet"): dieser Dienst läuft als LocalSystem - die einzige Stelle im
            // gesamten Update-Rollout, an der eine Registrierung mit Principal-GroupId
            // (BUILTIN\Users) garantiert nicht an fehlenden Windows-Adminrechten scheitert
            // (siehe AutostartRegistrar-Kommentar und installer/HaelpMiCommon.iss.inc). Für
            // Geräte, die VOR dem installer-seitigen Root-Cause-Fix installiert wurden (und
            // sich nur per Swap-Update aktualisieren, nie erneut über den Installer laufen -
            // CLAUDE.md "Programm-Updates laufen ausschließlich über die Swap-Pipeline"),
            // ist das die einzige Gelegenheit, einen kaputten/fehlenden Autostart-Eintrag
            // je noch elevated zu reparieren. Best-effort, kein Abbruch bei Fehlschlag -
            // der Agent versucht es beim eigenen Start ohnehin zusätzlich (unelevated) und
            // meldet einen verbleibenden Fehlschlag per Tray-Sprechblase an den Admin.
            if (!AutostartRegistrar.EnsureRegistered(newAgentPath, out var autostartError))
            {
                _logger.LogWarning("Autostart-Registrierung nach Swap-Update fehlgeschlagen: {Error}", autostartError);
            }

            Process.Start(new ProcessStartInfo(newAgentPath) { UseShellExecute = true });
        }

        // Deinstallation der Altversion - erst NACH erfolgreichem Verschieben löschen,
        // damit ein Fehler mittendrin die Altversion nicht schon vernichtet hätte.
        Directory.Delete(previousDir, true);
        Directory.Delete(newVersionDir, true);

        return new UpdateServiceResponse(true);
    }

    private UpdateServiceResponse Rollback(UpdateServiceRequest request)
    {
        StopTrackedTestProcess(request.Version);

        var dir = VersionDir(request.Version);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }

        return new UpdateServiceResponse(true);
    }

    private void StopTrackedTestProcess(string version)
    {
        if (!_testProcesses.Remove(version, out var process))
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Testinstanz für Version {Version} konnte nicht sauber beendet werden.", version);
        }
    }

    private static void KillProcessByName(string name)
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // best-effort - schon beendet oder kein Zugriff
            }
        }
    }

    // Räumt beim Dienststart Testinstanzen auf, die ein vorheriger (abgestürzter/
    // neugestarteter) Worker-Prozess über StartTest() angestoßen und danach nicht mehr
    // selbst beenden konnte - siehe Kommentar bei _testProcesses oben. Eine reguläre
    // Produktivinstanz läuft nie unterhalb von VersionsRootDir (nur ConfirmSwap verschiebt
    // Dateien von dort nach AppRoot und löscht das Versionsverzeichnis danach), daher ist
    // "HaelpMi.Agent.exe unterhalb von VersionsRootDir" ein eindeutiges Merkmal einer
    // herrenlosen Testinstanz und niemals ein Fehlalarm gegen eine echte Produktivinstanz.
    private void KillOrphanedTestInstances()
    {
        foreach (var process in Process.GetProcessesByName("HaelpMi.Agent"))
        {
            string? path;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch (Exception)
            {
                continue; // z. B. Zugriff auf ein fremdes Sitzungs-Handle verweigert - dann lieber nicht anfassen
            }

            if (path is null || !path.StartsWith(VersionsRootDir, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _logger.LogWarning(
                "Herrenlose Update-Testinstanz beim Dienststart gefunden und beendet: PID {Pid}, Pfad {Path}",
                process.Id, path);
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Herrenlose Testinstanz (PID {Pid}) konnte nicht beendet werden.", process.Id);
            }
        }
    }
}
