using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
    // Einzelinstanz-Sperre (Bugfix 11.08.2026, Fehlerbericht "Autostart nicht abgeschlossen -
    // Auto Task Registrierung fehlgeschlagen"): Crash-Log-Fund auf einem Testgerät
    // (PERSONALBÜRO, unmittelbar nach einem Swap-Update) zeigte eine SocketException "Only
    // one usage of each socket address" beim Binden von UpdatePackageDistributionService -
    // zwei HaelpMi.Agent.exe liefen gleichzeitig. Der Update-Dienst startet die neue Version
    // nach ConfirmSwapAsync per Process.Start, ohne zu prüfen, ob eine Alt-Instanz ihre
    // Sockets/den Autostart-Task schon vollständig freigegeben hat (UpdateServiceWorker.cs);
    // fällt das zeitlich zusammen mit einem Logon-Trigger-Start, laufen zwei Instanzen parallel
    // und konkurrieren dabei auch um denselben schtasks.exe-Aufruf für den Autostart-Task
    // (AutostartRegistrar.EnsureRegistered) - genau das erklärt die gemeldete Fehlermeldung.
    // "Local\" statt "Global\" wie beim analogen Schutz in HaelpMi.Config/App.xaml.cs: jede
    // angemeldete Sitzung bekommt weiterhin ihre eigene Instanz (AutostartRegistrar-Kommentar
    // "läuft in der jeweils eigenen Sitzung"), nur doppelte Starts INNERHALB derselben Sitzung
    // werden verhindert - das deckt den hier beobachteten Fall ab, ohne das Mehrbenutzer-Design
    // einzuschränken.
    private const string SingleInstanceMutexName = "Local\\HaelpMi.Agent.SingleInstance";

    private readonly SettingsStore _settingsStore = new();
    private readonly SharedConfigStore _sharedConfigStore = new();
    private readonly DeviceStore _deviceStore = new();
    // Nicht readonly, gleiches Muster wie _settings/_deployment unten (null! + Zuweisung in
    // OnStartup): braucht BuildIdentity(), also _settings/_deployment, die erst dort geladen
    // werden - kann daher kein Feld-Initialisierer sein. Lazy Device-Id-Provider (wie bei
    // DiscoveryService/ConfigSyncService) statt eines fixen Werts, siehe AuditLog-Klassendoku.
    private AuditLog _auditLog = null!;

    private AuditSyncService? _auditSyncService;

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

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
    private bool _autostartRegistered = true; // true = kein Registrierungsversuch nötig (unerwarteter leerer executablePath) oder erfolgreich
    private string? _autostartError;

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
            // Testinstanz (siehe Klassenkommentar bei RunUpdateSelfTestAndExit): läuft
            // absichtlich PARALLEL zu einer echten Produktivinstanz auf eigenen Ports -
            // die Einzelinstanz-Sperre unten gilt nur für den normalen Produktivpfad.
            RunUpdateSelfTestAndExit(testPort.Value);
            return;
        }

        if (ShouldRegisterAutostartOnly(e.Args))
        {
            // Root-Cause-Fix 11.08.2026 (Fehlerbericht "Autostart nicht eingerichtet" auf
            // einem normalen Win11-Rechner, Installation mit Admin-Rechten): [Run] startet
            // den eigentlichen Agent bewusst mit "runasoriginaluser" (siehe
            // installer/HaelpMiCommon.iss.inc-Kommentar dort) - der Prozess, der bisher als
            // EINZIGER AutostartRegistrar.EnsureRegistered aufrief, lief also NIE elevated,
            // selbst direkt nach einem Admin-Setup nicht. Ein Task mit Principal-GroupId
            // (BUILTIN\Users, siehe AutostartRegistrar.BuildTaskXml) lässt sich aber nur mit
            // Administratorrechten anlegen - das erklärt "Access is denied" unabhängig von
            // jeder Gruppenrichtlinie, auf jedem Rechner, auf dem der angemeldete Nutzer kein
            // lokaler Admin ist (der Normalfall laut CLAUDE.md: "ein Windows-Standardnutzer
            // kann App-Admin sein"). Dieser Modus wird stattdessen VOM INSTALLER selbst
            // aufgerufen, noch bevor er den echten Agent de-elevated startet (siehe [Run]) -
            // exakt derselbe AutostartRegistrar-Code, nur diesmal tatsächlich elevated.
            // Läuft ohne Fenster/Hintergrunddienste, meldet Erfolg/Fehlschlag nur per Exit-Code.
            RunRegisterAutostartOnlyAndExit();
            return;
        }

        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            // Schon eine Produktivinstanz in dieser Sitzung aktiv (siehe Feldkommentar oben) -
            // beenden statt um TCP-Ports und den Autostart-Task-Eintrag zu konkurrieren.
            Shutdown();
            return;
        }

        try
        {
            _deployment = DeploymentInfoStore.Load();
            _settings = _settingsStore.Load();
            // Erst jetzt sinnvoll konstruierbar (BuildIdentity() braucht _settings/_deployment,
            // siehe Feld-Kommentar oben) - die Lambda wird ohnehin erst beim ersten
            // tatsächlichen Append()-Aufruf ausgewertet, aber das Feld selbst muss vorher
            // zugewiesen sein (readonly).
            _auditLog = new AuditLog(() => BuildIdentity().DeviceId);
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

    // Separater Helfer statt Inline-Check (gleiches Muster wie ParseUpdateTestPort oben).
    // Kein eigener Unit-Test dafür (HaelpMi.Agent hat wie ParseUpdateTestPort daneben
    // grundsätzlich keine Testinfrastruktur/InternalsVisibleTo - eine würde hier isoliert
    // nur für diese eine Zeile aufgesetzt, das wäre Aufwand ohne Gegenwert). Der eigentliche
    // schtasks-Aufruf ist ohnehin System-verändernd und damit HaelpMi.Installer.Tests
    // vorbehalten (siehe TEST-STRATEGY.md) - Testfall dafür ist unten formuliert.
    private static bool ShouldRegisterAutostartOnly(string[] args) =>
        args.Contains("--register-autostart", StringComparer.Ordinal);

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

    /// <summary>
    /// Siehe Aufrufstelle in OnStartup: registriert nur den Autostart-Task und beendet sich
    /// sofort - kein Fenster, keine Netzwerkdienste, keine Einzelinstanz-Sperre (läuft kurz
    /// VOR dem eigentlichen Produktivprozess, würde sonst mit dessen Mutex kollidieren).
    /// Exit-Code 0 = registriert, 1 = fehlgeschlagen (der Installer wertet das nicht hart
    /// aus - schlägt es hier fehl, greift weiterhin der bestehende Selbstheilungsversuch im
    /// normalen Agent-Start plus die Tray-Meldung an den Admin als Rückfallebene).
    /// </summary>
    private void RunRegisterAutostartOnlyAndExit()
    {
        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        var ok = !string.IsNullOrEmpty(executablePath) && AutostartRegistrar.EnsureRegistered(executablePath, out _);
        Shutdown(ok ? 0 : 1);
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
        _ipcServer.On(IpcCommandType.ArmTestMode, HandleArmTestModeRequestAsync);
        _ipcServer.On(IpcCommandType.DisarmTestMode, HandleDisarmTestModeRequestAsync);
        _ipcServer.On(IpcCommandType.TestModeStatus, HandleTestModeStatusRequestAsync);
        _ipcServer.Start();

        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (!string.IsNullOrEmpty(executablePath))
        {
            // Best-effort (5.7): if GPO blocks task creation, the Agent still runs for
            // this session - see Pflichtenheft 6./8. for the known rollout risk.
            //
            // Bugfix 08.08.2026 (Fehlerbericht "Autostart nach Geräteneustart geht nicht"):
            // live nachgestellt - schtasks.exe /Create mit /SC ONLOGON kann mit "Access is
            // denied" fehlschlagen (in dieser Testumgebung reproduzierbar, unabhängig von
            // /RL; andere Trigger-Typen wie /SC ONCE funktionieren auf demselben Konto
            // problemlos - passt zu einer Richtlinieneinschränkung genau für
            // Anmelde-Trigger, wie im Kommentar oben schon vermutet). Der eigentliche Fehler
            // war nicht "es kann fehlschlagen" (das ist umgebungsabhängig, nicht reparierbar
            // ohne GPO-Änderung) - sondern dass ein Fehlschlag bisher komplett spurlos war
            // (out _ verwarf die Fehlermeldung). Jetzt gemerkt und nach InitializeTrayIcon()
            // sichtbar gemacht (Audit-Log + Tray-Sprechblase), damit ein Admin das
            // überhaupt bemerken und per GPO nachrüsten kann, statt es nie zu erfahren.
            _autostartRegistered = AutostartRegistrar.EnsureRegistered(executablePath, out _autostartError);
            if (!_autostartRegistered)
            {
                _auditLog.Append($"Autostart-Registrierung fehlgeschlagen: {_autostartError}");
            }
        }

        // Nutzerwunsch 14./15.08.2026 (revisionssicheres Audit-Log): Sendeseite läuft auf
        // JEDEM Gerät unabhängig von der Rolle (jedes Gerät hat ein eigenes AuditLog),
        // Empfangsseite (StartListening) dagegen nur, wenn dieses Gerät selbst Role.Admin
        // ist - gleiches Rollen-Gating wie beim Dashboard-Tray-Menüpunkt weiter unten
        // (InitializeTrayIcon), nur hier für den 24/7-Agent-Prozess statt das kurzlebige
        // Config.exe. Bewusst NICHT wie ConfigSyncService/EditLockService nur im
        // Config.exe-Dashboard-Prozess gestartet: ein Admin-Gerät soll Pushes auch
        // annehmen können, wenn das Dashboard-Fenster gerade gar nicht offen ist.
        _auditSyncService = new AuditSyncService(BuildIdentity, _auditLog, _auditLog.Append);
        if (_deployment.Role == Role.Admin)
        {
            _auditSyncService.StartListening();
        }

        _feedbackChannel = new AlarmFeedbackChannel(BuildIdentity, _auditLog.Append);
        _feedbackChannel.Start();

        _coordinator = new AlarmFlowCoordinator(BuildIdentity, () => _settings, _sharedConfigStore.LoadOrCreate, _feedbackChannel, _auditLog, _auditSyncService);

        // Multi-VLAN-Bridge-Seed (Nutzerwunsch 13.08.2026, Liste seit 15.08.2026):
        // SharedConfig gewinnt, weil sie hot-reload-editierbar ist (IPs können per DHCP
        // wandern, kein neuer Installer nötig) - deployment.json (Install-Creator-Startwert,
        // weiterhin nur eine einzelne Adresse) ist nur der Fallback für den allerersten
        // Start, solange noch nie ein Config-Sync stattgefunden hat (frisch installiertes
        // Gerät an einer Außenstelle).
        _discovery = new DiscoveryService(BuildIdentity, _auditLog.Append, bridgeSeedAddressProvider: () =>
        {
            var fromConfig = _sharedConfigStore.LoadOrCreate().BridgeSeedAddresses;
            if (fromConfig.Count > 0)
            {
                return fromConfig;
            }

            return string.IsNullOrWhiteSpace(_deployment.BridgeSeedAddress)
                ? Array.Empty<string>()
                : new[] { _deployment.BridgeSeedAddress! };
        });
        _discovery.StartListening();
        _ = _discovery.AnnounceAsync();

        // Nutzerwunsch 15.08.2026: Admin<->Admin-Mesh-Abgleich hängt am ohnehin
        // stattfindenden Boot-Call-Kontakt (siehe DiscoveryService.AdminPeerContactObserved-
        // Klassendoku) - das Event feuert dort ohnehin nur, wenn BEIDE Seiten Role.Admin
        // sind, ein Anhängen auf einem User-Gerät ist also harmlos (Handler wird nie
        // aufgerufen), keine zusätzliche Rollenprüfung hier nötig.
        _discovery.AdminPeerContactObserved += _auditSyncService.OnAdminPeerContactObserved;

        // Eigener Boot-Push (Nutzerwunsch 14.08.2026): mit dem beim letzten Beenden
        // gespeicherten Geräte-/Zustellstand, ohne auf die (fire-and-forget) Antworten des
        // gerade abgesetzten Announce oben zu warten - der nächste eigene Trigger (nächster
        // Alarm oder nächster Boot) holt jeden inzwischen neu erreichbaren Admin ab, kein
        // zusätzlicher Aufwand nötig, um exakt diesen einen Moment zu treffen.
        _ = _auditSyncService.PushPendingAsync(_deviceStore.Load());

        _listener = new AlarmTcpListener(BuildIdentity, _auditLog.Append);
        _listener.AlarmReceived += (_, args) => _coordinator.HandleIncomingAlarmRequest(args);
        _listener.Start();

        _configSync = new ConfigSyncService(BuildIdentity, _deviceStore.Load, _auditLog.Append);
        _configSync.ConfigApplied += (_, _) => RegisterHotkeysFromConfig();
        _configSync.Start();

        // Bugfix 11.08.2026 (Fehlerbericht "frisch installierte Geräte bleiben ohne
        // Config"): der Config-Sync-Broadcast in PublishAsync erreicht nur Geräte, die zum
        // Zeitpunkt der Admin-Änderung schon liefen - ein danach installiertes Gerät hört
        // ihn nie. Der Boot-Call tauscht die ConfigVersion ohnehin schon aus (siehe
        // DiscoveryService.PeerConfigVersionObserved); diese Verdrahtung macht daraus
        // zusätzlich einen Config-Pull-Trigger, symmetrisch für beide Seiten des Austauschs.
        _discovery.PeerConfigVersionObserved += _configSync.OnPeerConfigVersionObserved;

        var cacheStore = new UpdatePackageCacheStore();
        // Nutzerwunsch 09.08.2026: "vollautomatisch, sobald der Admin sich selbst aktualisiert
        // hat" - der Installer bringt dafür ein signiertes update-seed\ mit (siehe
        // UpdateSeedImporter). Vor dem Start von _updateDistribution, damit ein frisch
        // importiertes Paket ab der allerersten Anfrage eines Peers bedient werden kann.
        UpdateSeedImporter.TryImport(cacheStore, LiveIdentityFactory.CurrentProgramVersion, _auditLog.Append);
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

        // Erst jetzt möglich (Tray-Icon existiert erst ab hier) - siehe Kommentar bei der
        // Registrierung oben. Nur eine einmalige Sprechblase pro Prozessstart, nicht
        // wiederholt - Autostart-Status ändert sich innerhalb einer laufenden Sitzung nicht.
        if (!_autostartRegistered)
        {
            _trayIcon?.ShowBalloonTip(
                10000,
                "HälpMi - Autostart nicht eingerichtet",
                "HälpMi startet nach einem Geräteneustart NICHT automatisch (Task-Planer-Registrierung fehlgeschlagen, vermutlich durch eine Richtlinie blockiert). Bitte den Systemadministrator informieren - Details im lokalen Protokoll.",
                System.Windows.Forms.ToolTipIcon.Warning);
        }
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

    private Task<IpcResponse> HandleArmTestModeRequestAsync()
    {
        _coordinator!.ArmTestModeOnce();
        _auditLog.Append("testmodus scharfgeschaltet - gilt fuer den naechsten Hotkey-Alarm");
        return Task.FromResult(new IpcResponse(true, Remaining: AppConstants.TestModeTimeout));
    }

    private Task<IpcResponse> HandleDisarmTestModeRequestAsync()
    {
        // Nur protokollieren, wenn tatsächlich noch etwas scharf war - ein Disarm auf einen
        // längst abgelaufenen/nie scharfgeschalteten Zustand ist kein meldenswertes Ereignis.
        if (_coordinator!.TestModeRemaining is not null)
        {
            _auditLog.Append("testmodus manuell deaktiviert");
        }

        _coordinator!.DisarmTestMode();
        return Task.FromResult(new IpcResponse(true));
    }

    private Task<IpcResponse> HandleTestModeStatusRequestAsync() =>
        Task.FromResult(new IpcResponse(true, Remaining: _coordinator!.TestModeRemaining));

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
        _ = _auditSyncService?.DisposeAsync();
        _ = _discovery?.DisposeAsync();
        _ = _ipcServer?.DisposeAsync();

        if (_ownsSingleInstanceMutex)
        {
            // Nur freigeben, wenn diese Instanz sie auch erworben hat - dieselbe Regel wie
            // im analogen Schutz in HaelpMi.Config/App.xaml.cs (sonst SynchronizationLockException).
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
