using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using HaelpMi.Core.Autostart;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Interop;
using HaelpMi.Core.Ipc;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Runtime;
using HaelpMi.Core.Storage;
using HaelpMi.Core.Updates;

namespace HaelpMi.Agent;

/// <summary>
/// The always-running background piece (5.2 "Listener-Dienst"), kept alive as an
/// ordinary per-user process (not a Windows service) specifically so it can show UI on
/// the interactive desktop (5.3, avoiding Session-0 isolation). No MainWindow - this
/// process just pops transient windows (alarm popup, sender status) as needed.
///
/// Tray-Icon (Nutzerwunsch 05.08.2026): weicht bewusst von FR-4 ab - das Pflichtenheft sah
/// ursprünglich nur den Start-Menü-Weg vor, und ein Tray-Icon war laut Pflichtenheft 8a-3
/// vom Auftraggeber erst "für nach Abschluss des Alpha-Tests" angekündigt. Auf direkten
/// Wunsch schon jetzt umgesetzt statt erst danach - siehe InitializeTrayIcon().
///
/// Teil 2: Raum/Raumnummer/Geräte-ID/Rolle kommen ausschließlich vom Installer (FR-36 bis
/// FR-39) - es gibt keine App-eigene Ersteinrichtung mehr. Fehlt settings.json oder
/// deployment.json, ist das eine unvollständige Installation, kein normaler Startzustand
/// (siehe SettingsStore/DeploymentInfoStore) - der Prozess bricht dann mit einer klaren
/// Meldung ab, statt mit leeren/erfundenen Werten weiterzulaufen.
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly SettingsStore _settingsStore = new();
    private readonly SharedConfigStore _sharedConfigStore = new();
    private readonly DeviceStore _deviceStore = new();
    private readonly AuditLog _auditLog = new();

    private OwnSettings _settings = null!;
    private DeploymentInfo _deployment = null!;

    private DiscoveryService? _discovery;
    private AlarmTcpListener? _listener;
    private AlarmFeedbackChannel? _feedbackChannel;
    private ConfigSyncService? _configSync;
    private UpdatePackageDistributionService? _updateDistribution;
    private GlobalHotkey? _hotkey;
    private IpcServer? _ipcServer;
    private AlarmFlowCoordinator? _coordinator;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CrashLogger.InstallProcessWideHooks(nameof(HaelpMi.Agent));
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLogger.Log(nameof(HaelpMi.Agent), "DispatcherUnhandledException", args.Exception);
            // The Agent has no window of its own to lose - keep the background services
            // (listener, hotkey, IPC) alive rather than let one bad event handler kill the
            // whole 24/7 process (NFR-1).
            args.Handled = true;
        };

        var testPort = ParseUpdateTestPort(e.Args);
        if (testPort is not null)
        {
            RunUpdateSelfTestAndExit(testPort.Value);
            return;
        }

        try
        {
            _deployment = DeploymentInfoStore.Load();
            _settings = _settingsStore.Load();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or IOException)
        {
            // Vorher nur InvalidOperationException (fehlende Datei) abgefangen - eine
            // BESCHÄDIGTE settings.json/deployment.json (z. B. JsonException) flog bis zum
            // DispatcherUnhandledException-Handler durch, der zwar loggt und den Prozess am
            // Leben hält, aber OnStartup an dieser Stelle abbricht - ohne Meldung, ohne
            // Fenster, für den Nutzer nicht von einem echten Absturz zu unterscheiden.
            System.Windows.MessageBox.Show(
                $"Die Konfigurationsdatei ist beschädigt oder unvollständig:{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Bitte HälpMi neu installieren/reparieren.",
                "HälpMi - Installation unvollständig", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        StartBackgroundServices();
    }

    private static int? ParseUpdateTestPort(string[] args)
    {
        const string prefix = "--update-test-port=";
        var arg = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
        return arg is not null && int.TryParse(arg[prefix.Length..], out var port) ? port : null;
    }

    /// <summary>
    /// Testmodus für die Update-Pipeline (Abschnitt 11, per HaelpMi.UpdateService
    /// gestartet): prüft NUR, ob dieser Build startet und die bestehende Installation
    /// (settings.json/deployment.json) lesen kann - bindet bewusst KEINEN der echten
    /// Netzwerk-Dienste (Discovery/Alarm/ConfigSync/...), sendet keinen Boot-Call,
    /// registriert keinen Autostart. Ein vollwertiger Parallelbetrieb auf Alternativ-
    /// Ports würde echten Netzwerkverkehr mit identischer Geräte-/Kunden-Gruppen-ID
    /// erzeugen und andere Geräte im Netz verwirren - für "startet der neue Build
    /// überhaupt" reicht ein isolierter Selbsttest, der lokal antwortet, sobald der
    /// UpdateOrchestrator (auf der Produktivinstanz) danach fragt.
    /// </summary>
    private void RunUpdateSelfTestAndExit(int testPort)
    {
        TcpListener? listener = null;
        try
        {
            var settings = _settingsStore.Load();
            var deployment = DeploymentInfoStore.Load();
            _ = LiveIdentityFactory.Create(settings, deployment);

            listener = new TcpListener(IPAddress.Loopback, testPort);
            listener.Start();

            var acceptTask = listener.AcceptTcpClientAsync();
            if (!acceptTask.Wait(TimeSpan.FromSeconds(30)))
            {
                return; // niemand hat innerhalb der Frist angefragt - einfach beenden, zählt als Fehlschlag
            }

            using var client = acceptTask.Result;
            using var stream = client.GetStream();
            var okBytes = System.Text.Encoding.UTF8.GetBytes("OK\n");
            stream.Write(okBytes);
        }
        catch (Exception)
        {
            // Fehlgeschlagener Selbsttest: ohne Antwort beenden - der Orchestrator wertet
            // "keine/ungültige Antwort innerhalb des Timeouts" als Fehlschlag.
        }
        finally
        {
            listener?.Stop();
            Shutdown();
        }
    }

    private LiveIdentity BuildIdentity() => LiveIdentityFactory.Create(_settings, _deployment);

    private void StartBackgroundServices()
    {
        // Bugfix 06.08.2026 (Fehlerbericht "Dashboard startet nicht" + Crash-Log-Fund:
        // AlarmTcpListener.Start SocketException "Only one usage of each socket address"
        // auf einer KOMPLETT FRISCHEN VM ohne jede Vorinstallation - also kein Altprozess-
        // Problem, sondern ein Race): der IPC-Server (für Config.exe's "läuft der Agent
        // schon?"-Ping, siehe HaelpMi.Config/App.xaml.cs EnsureAgentIsRunningAsync) stand
        // bisher GANZ AM ENDE dieser Methode - nach Discovery/Alarm-Listener/ConfigSync/
        // Update-Verteilung/Hotkeys, lauter Schritten, die spürbar Zeit brauchen. Der
        // [Run]-Abschnitt des Installers startet Agent.exe UND Config.exe (mit
        // --open-dashboard) fast gleichzeitig (beide "nowait", siehe
        // installer/HaelpMiCommon.iss.inc). Traf Config.exe's Ping in dieser Lücke ein,
        // bevor der IPC-Server überhaupt lief, wertete Config.exe das fälschlich als
        // "Agent läuft nicht" und startete einen ZWEITEN Agent-Prozess - der kollidierte
        // dann beim Binden des Alarm-TCP-Ports mit dem ersten (genau die geloggte
        // Exception). Jetzt: IPC-Server so früh wie möglich starten. Die drei zusätzlichen
        // Handler werden weiterhin VOR Start() registriert (unverändert sicher) - sie
        // greifen auf _discovery/_coordinator erst beim tatsächlichen Aufruf zu, und diese
        // rein synchrone (nicht awaitete) Methode hat beide längst gesetzt, bevor der
        // AcceptLoop nach dem Verlassen dieser Methode überhaupt die erste Chance bekommt,
        // eine echte Anfrage zu verarbeiten.
        _ipcServer = new IpcServer();
        _ipcServer.On(IpcCommandType.Rebroadcast, HandleRebroadcastRequestAsync);
        _ipcServer.On(IpcCommandType.SearchAgain, HandleSearchAgainRequestAsync);
        _ipcServer.On(IpcCommandType.SelfTest, HandleSelfTestRequestAsync);
        _ipcServer.Start();

        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (!string.IsNullOrEmpty(executablePath))
        {
            // Best-effort (5.7): if GPO blocks task creation, the Agent still runs for
            // this session - see Pflichtenheft 6./8. for the known rollout risk.
            AutostartRegistrar.EnsureRegistered(executablePath, out _);
        }

        _feedbackChannel = new AlarmFeedbackChannel(BuildIdentity, _auditLog.Append);
        _feedbackChannel.Start();

        _coordinator = new AlarmFlowCoordinator(BuildIdentity, () => _settings, _sharedConfigStore.LoadOrCreate, _feedbackChannel);

        _discovery = new DiscoveryService(BuildIdentity, _auditLog.Append);
        _discovery.StartListening();
        _ = _discovery.AnnounceAsync();

        _listener = new AlarmTcpListener(BuildIdentity, _auditLog.Append);
        _listener.AlarmReceived += (_, args) => _coordinator.HandleIncomingAlarmRequest(args);
        _listener.Start();

        _configSync = new ConfigSyncService(BuildIdentity, _deviceStore.Load, _auditLog.Append);
        _configSync.ConfigApplied += (_, _) => RegisterHotkeysFromConfig();
        _configSync.Start();

        var cacheStore = new UpdatePackageCacheStore();
        _updateDistribution = new UpdatePackageDistributionService(BuildIdentity, cacheStore, _auditLog.Append);
        _updateDistribution.Start();

        var orchestrator = new UpdateOrchestrator(
            BuildIdentity,
            _deviceStore.Load,
            _sharedConfigStore.LoadOrCreate,
            _updateDistribution,
            cacheStore,
            new UpdateServiceIpcClient(),
            new UpdateLockoutStore(),
            _auditLog.Append);
        _discovery.PeerVersionObserved += orchestrator.OnPeerVersionObserved;

        _hotkey = new GlobalHotkey();
        _hotkey.ProfilePressed += OnProfileHotkeyPressed;
        RegisterHotkeysFromConfig();

        InitializeTrayIcon();
    }

    // Nutzerwunsch 05.08.2026: Tray-Icon zum Öffnen von Konfiguration/Dashboard - siehe
    // Klassenkommentar oben für den Hintergrund. Icon wird aus der laufenden exe selbst
    // extrahiert (ExtractAssociatedIcon) statt aus einer losen Assets-Datei zu laden - die
    // ist über ApplicationIcon nur ins exe-Ressourcen eingebettet, nicht zwingend als
    // eigenständige Datei im Installationsordner vorhanden.
    private void InitializeTrayIcon()
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        var icon = (exePath is not null ? System.Drawing.Icon.ExtractAssociatedIcon(exePath) : null)
            ?? System.Drawing.SystemIcons.Application;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        // Nutzerwunsch 06.08.2026: "HälpMi öffnen" beschrieb nicht, was dahinter passiert -
        // für den User-Fall (eigene Hotkeys/Empfängerkreise) ist "konfigurieren" treffender.
        menu.Items.Add("HälpMi konfigurieren", null, (_, _) => OpenConfigOrDashboard());
        if (_deployment.Role == Role.Admin)
        {
            // Nutzerwunsch 06.08.2026: Admin soll das Dashboard direkt aus dem Tray
            // erreichen, ohne erst über die normale Konfiguration den dortigen Dashboard-
            // Button suchen zu müssen - identischer Aufruf wie der Start-Menü-Eintrag
            // "HälpMi Dashboard" (siehe installer/HaelpMiCommon.iss.inc).
            menu.Items.Add("Dashboard öffnen", null, (_, _) => OpenDashboardDirectly());
        }

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "HälpMi",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => OpenConfigOrDashboard();
    }

    // Startet HaelpMi.Config.exe genau wie der Start-Menü-Eintrag - öffnet je nach Rolle
    // entweder die normale Konfiguration oder (über den dortigen Button) das Dashboard;
    // keine eigene Logik hier nötig, das Konfigurationsprogramm entscheidet selbst.
    private static void OpenConfigOrDashboard()
    {
        var configExePath = Path.Combine(AppContext.BaseDirectory, "HaelpMi.Config.exe");
        if (!File.Exists(configExePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(configExePath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // best-effort - der Agent hat kein eigenes Fenster, um einen Fehler anzuzeigen
        }
    }

    // Nutzerwunsch 06.08.2026: separater Tray-Menüpunkt für Admins, direkter Sprung ins
    // Dashboard statt über die normale Konfiguration - gleicher Aufruf wie der Start-Menü-
    // Eintrag ("--open-dashboard", siehe HaelpMi.Config/App.xaml.cs OnStartup).
    private static void OpenDashboardDirectly()
    {
        var configExePath = Path.Combine(AppContext.BaseDirectory, "HaelpMi.Config.exe");
        if (!File.Exists(configExePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(configExePath, "--open-dashboard") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // best-effort - der Agent hat kein eigenes Fenster, um einen Fehler anzuzeigen
        }
    }

    private void RegisterHotkeysFromConfig()
    {
        var config = _sharedConfigStore.LoadOrCreate();
        var errors = _hotkey!.ReplaceAll(config.AlarmProfiles.Select(p => (p.Id, p.Hotkey)));
        foreach (var (profileId, error) in errors)
        {
            _auditLog.Append($"hotkey registration failed for profile={profileId}: {error}");
        }
    }

    private void OnProfileHotkeyPressed(Guid profileId)
    {
        var config = _sharedConfigStore.LoadOrCreate();
        var profile = config.AlarmProfiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is not null)
        {
            _coordinator!.TriggerAlarmProfile(profile);
        }
    }

    private async Task<IpcResponse> HandleRebroadcastRequestAsync()
    {
        _settings = _settingsStore.Load(); // pick up whatever Config just saved
        await _discovery!.AnnounceAsync();
        return new IpcResponse(true);
    }

    private async Task<IpcResponse> HandleSearchAgainRequestAsync()
    {
        await _discovery!.AnnounceAsync();
        return new IpcResponse(true);
    }

    private async Task<IpcResponse> HandleSelfTestRequestAsync()
    {
        var config = _sharedConfigStore.LoadOrCreate();
        var profile = config.AlarmProfiles.FirstOrDefault();
        if (profile is null)
        {
            return new IpcResponse(false, "Kein Alarm-Profil konfiguriert.");
        }

        var ok = await _coordinator!.SendSelfTestAsync(profile);
        return new IpcResponse(ok);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Ohne explizites Verstecken bleibt ein NotifyIcon nach Prozessende manchmal bis
        // zum nächsten Mausüberfahren als "Geister-Icon" im Tray stehen (bekanntes Windows-
        // Verhalten, kein HälpMi-Bug) - Visible=false vor Dispose räumt zuverlässig auf.
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _hotkey?.Dispose();
        _ = _listener?.DisposeAsync();
        _ = _feedbackChannel?.DisposeAsync();
        _ = _configSync?.DisposeAsync();
        _ = _updateDistribution?.DisposeAsync();
        _ = _discovery?.DisposeAsync();
        _ = _ipcServer?.DisposeAsync();
        base.OnExit(e);
    }
}
