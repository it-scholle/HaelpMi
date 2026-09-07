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
using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Runtime;
using HaelpMi.Core.Storage;
using HaelpMi.UI.Windows;

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
    private readonly AuditLog _auditLog = new();

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    private OwnSettings _settings = null!;
    private DeploymentInfo _deployment = null!;

    /// <summary>
    /// Einmal beim Start gelesen (Lizenzdatei ändert sich nie im laufenden Betrieb, anders
    /// als die Config) - Soft-Expiry/Invalid/Missing blockieren den Start nicht (CLAUDE.md),
    /// stehen aber für die Ablaufwarnung aus Issue #20 zur Verfügung.
    /// </summary>
    private LicenseCheckResult _license = null!;

    /// <summary>Nur gesetzt, während das Systemstart-Erinnerungs-Popup offen ist - siehe HandleLicenseRenewedRequestAsync.</summary>
    private LicenseReminderToastWindow? _licenseReminderToast;

    /// <summary>Issue #59/#60: nur gesetzt, während dieses Gerät als lizenzüberschritten gilt - siehe RefreshLicenseLimitState.</summary>
    private LicenseLimitToastWindow? _licenseLimitToast;

    private DiscoveryService? _discovery;
    private AlarmChannel? _listener;
    private AlarmFeedbackChannel? _feedbackChannel;
    private ConfigSyncService? _configSync;
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
            _license = LicenseReader.Load(_deployment.CustomerGroupId, Convert.FromHexString(_deployment.LicensePublicKeyHex));
        }
        // FormatException: LicensePublicKeyHex (Issue #56) ist kein gültiger Hex-String -
        // dieselbe Fehlerklasse wie ein beschädigtes deployment.json, nicht separat zu werten.
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or IOException or FormatException)
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
        ShowLicenseReminderToastIfNeeded(isPostInstallStart: IsPostInstallStart(e.Args));
        RefreshLicenseLimitState();
    }

    // Issue #20-Nacharbeit (Nutzerfrage 02.09.2026, "das Popup beim Systemstart - ist das
    // schon implementiert?"): war es nicht - #20 deckte bisher nur den Dashboard-Banner ab
    // (nur sichtbar, wenn der Admin das Dashboard ohnehin öffnet), nicht die von Anfang an
    // im Ticket beschriebene proaktive Erinnerung. Nur wenn der Agent-Prozess dieser
    // Windows-Sitzung das Dashboard überhaupt öffnen dürfte (Issue #10 - das Popup verweist
    // aufs Dashboard, sonst irreführend für einen anderen Nutzer nach Fast User Switching)
    // und nur, wenn "Später erinnern" nicht noch aktiv ist.
    //
    // Issue #68: der Installer startet den Agent selbst einmalig direkt nach der
    // Installation (HaelpMiCommon.iss.inc [Run], mit --post-install) - ohne diesen Filter
    // poppte das Popup schon während der Nutzer noch im Installationsfenster war, obwohl
    // eine frische Lizenz zu diesem Zeitpunkt ohnehin meist noch gar nicht eingespielt ist.
    // Dieser eine installer-getriggerte Start wird deshalb übersprungen; jeder spätere
    // (echte) Agent-Start - ob per Autostart-Task beim nächsten Neustart/Login oder manuell -
    // prüft wie gehabt.
    private void ShowLicenseReminderToastIfNeeded(bool isPostInstallStart)
    {
        if (isPostInstallStart || !DashboardAccessGuard.CurrentUserMayOpenDashboard(_deployment.Role) || LicenseReminderStateStore.IsSnoozed(DateTime.UtcNow))
        {
            return;
        }

        var warning = LicenseWarningEvaluator.Evaluate(_license, DateTime.UtcNow);
        if (warning.Level == LicenseWarningLevel.None)
        {
            return;
        }

        _licenseReminderToast = new LicenseReminderToastWindow(warning, OpenDashboardDirectly);
        _licenseReminderToast.Closed += (_, _) => _licenseReminderToast = null;
        _licenseReminderToast.Show();
    }

    // Nutzerbericht 03.09.2026: nach erfolgreichem "Lizenz einspielen" im Admin-Dashboard
    // (separater Prozess, siehe HaelpMi.Config) blieb ein noch offenes Erinnerungs-Popup
    // veraltet stehen - "sobald eine aktive Lizenz erscheint, müssen die veralteten
    // Meldungen automatisch verschwinden". Config schickt dafür IpcCommandType.LicenseRenewed.
    private Task<IpcResponse> HandleLicenseRenewedRequestAsync()
    {
        _licenseReminderToast?.Close();

        // Issue #59/#60: ohne Neuladen bliebe eine gerade erst hochgesetzte UserLimit-Grenze
        // bis zum nächsten Programmneustart wirkungslos - "Aktivierung ... durch neue
        // Lizenz" (Issue #59) braucht den frischen Stand sofort, nicht erst danach.
        _license = LicenseReader.Load(_deployment.CustomerGroupId, Convert.FromHexString(_deployment.LicensePublicKeyHex));
        RefreshLicenseLimitState();

        // Nutzerwunsch 07.09.2026 ("Update soll sofort an alle Geräte verteilt werden"): der
        // nächste Announce führt die frisch importierte Lizenz jetzt schon mit (siehe
        // BuildIdentity-Verdrahtung von DiscoveryService unten) - nicht erst beim nächsten
        // natürlichen Boot-Call warten, sondern sofort selbst einen anstoßen.
        _ = _discovery?.AnnounceAsync();
        return Task.FromResult(new IpcResponse(true));
    }

    /// <summary>
    /// Issue #59/#60-Nachtrag "Lizenz sofort verteilen": ein Peer hat in seinem Boot-Call
    /// eine Lizenz mitgeteilt - eigenständig nachprüfen (Signatur + Kundengruppe, siehe
    /// LicenseImporter.TryAdoptFromPeer) und nur bei echtem Zugewinn (keine eigene Lizenz
    /// oder die mitgeteilte ist neuer) übernehmen. Betrifft typischerweise ein Gerät, das
    /// bisher als lizenzlos/lizenzüberschritten galt und jetzt durch die frisch importierte
    /// Lizenz des Admins wieder ins Kontingent passt.
    /// </summary>
    private void OnPeerLicenseObserved(object? sender, PeerLicenseInfo info)
    {
        var adopted = LicenseImporter.TryAdoptFromPeer(
            info.LicenseKeyText, _deployment.CustomerGroupId, Convert.FromHexString(_deployment.LicensePublicKeyHex), _license.License);
        if (!adopted)
        {
            return;
        }

        _license = LicenseReader.Load(_deployment.CustomerGroupId, Convert.FromHexString(_deployment.LicensePublicKeyHex));
        RefreshLicenseLimitState();
        _auditLog.Append($"Lizenz von Peer deviceId={info.DeviceId} übernommen (IssuedAtUtc={_license.License?.IssuedAtUtc:O})");

        // Weiterverbreiten (Gossip-Welle, wie Config-Sync): Peers, die diesen direkten
        // Kontakt verpasst haben, lernen die Lizenz beim nächsten Announce von UNS.
        _ = _discovery?.AnnounceAsync();
    }

    /// <summary>
    /// Issue #59/#60: zeigt/schließt den Lizenzlimit-Toast passend zum aktuellen
    /// Deaktivierungs-Zustand dieses Geräts - aufgerufen beim Start, bei jedem Boot-Call-
    /// Update (neues Gerät gesehen, Kontingent könnte sich dadurch geändert haben) und nach
    /// einem frisch eingespielten Lizenz-Update.
    /// </summary>
    private void RefreshLicenseLimitState()
    {
        var disabled = _coordinator!.IsOwnDeviceLicenseDisabled();

        // Bugfix 07.09.2026: BeginInvoke statt Invoke - dieser Aufruf kann von
        // DiscoveryService's Empfangs-Thread aus kommen (DeviceUpdated/PeerLicenseObserved),
        // SYNCHRON innerhalb von HandleDatagramAsync. Ein blockierendes Invoke hätte dort im
        // ungünstigen Fall (UI-Thread gerade mit etwas anderem beschäftigt) die gesamte
        // Boot-Call-Verarbeitung dieses einen Empfangs-Threads verzögert/blockiert - bei
        // einem UDP-Empfangsloop, der Nachrichten sequenziell abarbeitet, wirkt sich das auf
        // ALLE folgenden Boot-Calls aus, nicht nur auf diesen einen. BeginInvoke reiht die
        // UI-Arbeit nur ein und kehrt sofort zurück.
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (disabled && _licenseLimitToast is null)
            {
                Action? openDashboard = _deployment.Role == Role.Admin ? OpenDashboardDirectly : null;
                _licenseLimitToast = new LicenseLimitToastWindow(openDashboard);
                _licenseLimitToast.Closed += (_, _) => _licenseLimitToast = null;
                _licenseLimitToast.Show();
            }
            else if (!disabled)
            {
                _licenseLimitToast?.Close();
            }
        });
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

    // Issue #68: gesetzt nur auf dem Agent-Start, den der Installer selbst direkt nach der
    // Installation auslöst (siehe HaelpMiCommon.iss.inc [Run]) - siehe
    // ShowLicenseReminderToastIfNeeded-Kommentar dort für den Grund.
    private static bool IsPostInstallStart(string[] args) =>
        args.Contains("--post-install", StringComparer.Ordinal);

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
        _ipcServer.On(IpcCommandType.LicenseRenewed, HandleLicenseRenewedRequestAsync);
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

        _feedbackChannel = new AlarmFeedbackChannel(BuildIdentity, _auditLog.Append);
        _feedbackChannel.Start();

        _coordinator = new AlarmFlowCoordinator(BuildIdentity, () => _settings, _sharedConfigStore.LoadOrCreate, () => _license, _feedbackChannel);

        _discovery = new DiscoveryService(BuildIdentity, _auditLog.Append, ownLicenseKeyTextProvider: () =>
            _license.License is null ? null : LicenseKeyText.Encode(_license.License));
        _discovery.StartListening();
        _ = _discovery.AnnounceAsync();

        // Issue #59/#60: jeder neu bekannt gewordene Peer kann das eigene Lizenzkontingent
        // verschieben (mehr bekannte Geräte, evtl. auch ein neuer Peer mit früherem
        // FirstSeenUtc über Gossip) - Zustand nach jedem Boot-Call-Update neu bewerten.
        _discovery.DeviceUpdated += (_, _) => RefreshLicenseLimitState();

        // Issue #59/#60-Nachtrag "Lizenz sofort verteilen".
        _discovery.PeerLicenseObserved += OnPeerLicenseObserved;

        // Issue #9 (Fast User Switching ohne Logout/Reboot): AlarmChannel entscheidet
        // selbst, ob diese Sitzung den echten TCP-Port hält (Primary) oder als Satellite
        // über den lokalen Relay-Kanal einer anderen Sitzung mitläuft - für den
        // Aufrufer hier kein Unterschied, AlarmReceived feuert in beiden Rollen gleich.
        _listener = new AlarmChannel(BuildIdentity, _auditLog.Append, () => _coordinator!.IsOwnDeviceLicenseDisabled());
        _listener.AlarmReceived += (_, args) => _coordinator.HandleIncomingAlarmRequest(args);
        _listener.Start();

        _configSync = new ConfigSyncService(BuildIdentity, _deviceStore.Load, _auditLog.Append);
        _configSync.ConfigApplied += (_, _) =>
        {
            RegisterHotkeysFromConfig();

            // Nutzerwunsch 20.08.2026 ("fliegender Configaustausch"): sobald dieses Gerät
            // eine neue Config übernommen hat (egal ob per dediziertem Config-Sync-Announce
            // oder per Boot-Call-Nachzieh-Pull, siehe PeerConfigVersionObserved-Verdrahtung
            // unten), sofort den ganz normalen Discovery-Announce erneut ausstrahlen - der
            // führt identity.ConfigVersion ohnehin schon mit (DiscoveryService.BuildMessage).
            // Andere Geräte, die das ursprüngliche Announce verpasst haben (offline, gerade
            // erst gestartet, ...), lernen die neue Version dadurch von UNS statt erst beim
            // eigenen nächsten Boot-Call zu warten - PeerConfigVersionObserved löst bei
            // ihnen denselben Pull aus wie ein regulärer Boot-Call. Macht aus dem einzelnen
            // Config-Sync-Broadcast eine Welle, die sich Gerät für Gerät weiterträgt, statt
            // nur die zufällig zum Zeitpunkt der Änderung online gewesenen zu erreichen.
            _ = _discovery!.AnnounceAsync();
        };
        _configSync.Start();

        // Bugfix 11.08.2026 (Fehlerbericht "frisch installierte Geräte bleiben ohne
        // Config"): der Config-Sync-Broadcast in PublishAsync erreicht nur Geräte, die zum
        // Zeitpunkt der Admin-Änderung schon liefen - ein danach installiertes Gerät hört
        // ihn nie. Der Boot-Call tauscht die ConfigVersion ohnehin schon aus (siehe
        // DiscoveryService.PeerConfigVersionObserved); diese Verdrahtung macht daraus
        // zusätzlich einen Config-Pull-Trigger, symmetrisch für beide Seiten des Austauschs.
        _discovery.PeerConfigVersionObserved += _configSync.OnPeerConfigVersionObserved;

        // Nutzerwunsch 20.08.2026 ("die 16 soll das Update-Ei und die Update-Pipeline
        // komplett ignorieren, nur manueller Installer und manuelle Updates"): die
        // gesamte automatische Update-Verteilung/-Orchestrierung (UpdatePackageDistribution
        // Service, UpdateSeedImporter, UpdateOrchestrator, Boot-Call-Programmversions-
        // Abgleich über PeerVersionObserved) läuft auf diesem Vorstellungsversion-Branch
        // absichtlich nicht mehr mit - weder gebaut/verteilt (Install-Creator/RefreshPayload
        // Async lässt HaelpMi.UpdateService seither aus, siehe dort) noch hier zur Laufzeit
        // gestartet. Ein Update dieses Demo-Standes läuft ausschließlich manuell: neuer
        // Installer-Lauf pro Gerät. HaelpMi.UpdateService/UpdateSigner-Quellcode bleibt im
        // Repo (bewusst nicht gelöscht, keine unnötig invasive Änderung), wird für diesen
        // Branch nur nirgends mehr eingebunden/gestartet.

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
        if (DashboardAccessGuard.CurrentUserMayOpenDashboard(_deployment.Role))
        {
            // Nutzerwunsch 06.08.2026: Admin soll das Dashboard direkt aus dem Tray
            // erreichen, ohne erst über die normale Konfiguration den dortigen Dashboard-
            // Button suchen zu müssen - identischer Aufruf wie der Start-Menü-Eintrag
            // "HälpMi Dashboard" (siehe installer/HaelpMiCommon.iss.inc). Issue #10: nur
            // beim installierenden Windows-Nutzer - der Agent läuft laut Issue #9 zwar für
            // jede angemeldete Sitzung, dieser Menüpunkt darf dort trotzdem nicht auftauchen.
            menu.Items.Add("Dashboard öffnen", null, (_, _) => OpenDashboardDirectly());
        }
        // Issue #6: allen Rollen zugänglich, deshalb außerhalb des Admin-Ifs oben - anders
        // als Konfiguration/Dashboard braucht "Über mich" keinen eigenen Prozess
        // (HaelpMi.Config.exe), da der Agent HaelpMi.UI ohnehin schon referenziert.
        menu.Items.Add("Über HälpMi", null, (_, _) => new AboutWindow(_deployment).Show());

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

        // Bugfix 26.08.2026 (Fehlerbericht "Admin-Gerät sendet neues/geändertes
        // Alarmprofil erst nach Neustart"): dieser Handler ist der einzige Weg, auf dem
        // das EIGENE Admin-Gerät eine gerade selbst veröffentlichte Config-Änderung
        // mitbekommt (NotifyLocalAgentOfConfigChange in HaelpMi.Config) - der reguläre
        // ConfigSyncService.ConfigApplied-Pfad, der RegisterHotkeysFromConfig() sonst
        // aufruft, feuert für die eigene Änderung nie (der lokale Agent verwirft seinen
        // eigenen Broadcast als Echo, gleiche DeviceId). Ohne diesen Aufruf blieb ein neu
        // angelegtes Profil auf dem sendenden Admin-Gerät selbst ohne registrierte Hotkey-
        // Bindung, obwohl jedes andere Gerät die Änderung sofort bekam.
        RegisterHotkeysFromConfig();

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
        if (_coordinator!.IsOwnDeviceLicenseDisabled())
        {
            return new IpcResponse(false, "Lizenz ausgeschöpft - dieses Gerät ist deaktiviert und kann keine Alarme senden.");
        }

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
