using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Interop;
using HaelpMi.Core.Ipc;
using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Runtime;
using HaelpMi.Core.Storage;
using HaelpMi.UI.ViewModels;
using HaelpMi.UI.Windows;

namespace HaelpMi.Config;

/// <summary>
/// The Konfigurationsprogramm (FR-16): a separate, on-demand process from the always
/// running Agent, reached via the Start Menu shortcut (FR-4). Teil 2: Raum/Raumnummer/
/// Geräte-ID/Rolle kommen ausschließlich vom Installer (FR-36 bis FR-39) - es gibt keine
/// App-eigene Ersteinrichtung mehr. Fehlt settings.json, ist das eine unvollständige
/// Installation, kein normaler Startzustand (siehe SettingsStore) - der Prozess bricht
/// dann mit einer klaren Meldung ab, statt mit leeren/erfundenen Werten weiterzulaufen.
/// </summary>
public partial class App : System.Windows.Application
{
    // Einzelinstanz-Sperre (Nutzerwunsch 06.08.2026): Startmenü-Verknüpfung, Tray-
    // Doppelklick und "HälpMi öffnen" im Kontextmenü können alle denselben Prozess mehrfach
    // anstoßen - ohne Sperre öffnet jeder Klick ein weiteres Fenster samt eigenem
    // ConfigSync/EditLock/IPC-Client (unnötige Netzwerk-Last, verwirrende doppelte Fenster).
    // "Local\" statt "Global\", damit jede RDP-Sitzung (SM_REMOTESESSION) ihre eigene
    // Instanz bekommt statt sich mit der Konsolen-Sitzung eine Sperre zu teilen - passt zum
    // bestehenden Datenschutz-/Sitzungs-Prinzip (RDP ist eine reine Sitzungseigenschaft).
    private const string SingleInstanceMutexName = "Local\\HaelpMi.Config.SingleInstance";
    private const string ActivateEventName = "Local\\HaelpMi.Config.ActivateRequest";
    // Bugfix 06.08.2026 ("Dashboard startet gar nicht mehr"): eine zweite Instanz mit
    // --open-dashboard wurde bisher wie jede andere zweite Instanz behandelt - sie hat
    // einfach das schon offene Fenster der ersten Instanz nach vorne geholt, UNABHÄNGIG
    // davon, ob das das gewünschte Dashboard war oder z. B. nur das normale
    // Konfigurationsfenster (offen z. B. weil zuvor "HälpMi öffnen" im Tray-Menü geklickt
    // wurde). Für den Nutzer sah das aus wie "Dashboard öffnet gar nicht" - es kam ja
    // wirklich nichts Neues, nur ein bereits sichtbares falsches Fenster wurde refokussiert.
    // Jetzt eigenes Signal dafür, das die erste Instanz notfalls ein echtes Dashboard-
    // Fenster NACHTRÄGLICH öffnet, statt nur zu foregrounden was zufällig schon offen ist.
    private const string ActivateDashboardEventName = "Local\\HaelpMi.Config.ActivateDashboardRequest";

    private ConfigSyncService? _configSync;
    private EditLockService? _editLock;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private EventWaitHandle? _activateEvent;
    private EventWaitHandle? _activateDashboardEvent;
    private AdminDashboardWindow? _dashboardWindow;

    // Für das nachträgliche Öffnen des Dashboards aus WaitForActivationRequests gebraucht
    // (dieselben Objekte wie im normalen --open-dashboard-Startpfad unten) - SettingsStore/
    // DeviceStore sind zustandslose Wrapper (sicher wiederzuverwenden), DeploymentInfo ist
    // nach der Installation unveränderlich.
    private SettingsStore? _settingsStore;
    private DeploymentInfo? _deployment;
    private DeviceStore? _deviceStore;

    /// <summary>Wie beim Agent (siehe dortiger Feldkommentar) - Grundlage für Issue #20.</summary>
    private LicenseCheckResult? _license;

    // async void statt async Task: OnStartup ist ein void-Override (Application-Basisklasse),
    // dasselbe etablierte WPF-Muster wie bei einem async Button_Click-Handler.
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Nutzer-Repro 25.08.2026 (Issue #1): Auswahl selbst funktioniert, aber die
        // Hover-Vorschau (welcher Eintrag beim Überfahren markiert würde) bleibt in VM/
        // RDP-Sitzungen ohne echtes Monitor-/Vsync-Signal aus - WPFs Compositor-Thread
        // bekommt dort keinen Takt für die vielen kleinen Repaints beim Mausbewegen,
        // größere Repaints (z. B. beim Schließen des Popups) laufen dagegen normal durch.
        // SoftwareOnly nutzt einen eigenen Rasterizer-Pfad ohne diese Abhängigkeit vom
        // fehlenden Vsync-Signal - muss vor jeder Fenstererzeugung gesetzt werden.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        base.OnStartup(e);

        CrashLogger.InstallProcessWideHooks(nameof(HaelpMi.Config));
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLogger.Log(nameof(HaelpMi.Config), "DispatcherUnhandledException", args.Exception);
            // Keep the window open rather than let one bad refresh/click take the whole
            // Konfigurationsprogramm down - the user can just retry the action.
            args.Handled = true;
        };

        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            // Schon eine laufende Instanz da: statt blind irgendein schon offenes Fenster
            // nach vorne zu holen, erst feststellen, OB dieser Aufruf explizit das Dashboard
            // will (--open-dashboard, Admin-Rolle) - siehe ActivateDashboardEventName oben.
            // Best-effort/try-catch: das Laden von settings.json darf diesen ohnehin schon
            // kurzen Beende-Pfad nicht zum Absturz bringen, dann eben normal aktivieren.
            var wantsDashboard = false;
            if (e.Args.Contains("--open-dashboard"))
            {
                try
                {
                    wantsDashboard = DashboardAccessGuard.CurrentUserMayOpenDashboard(new SettingsStore().Load().Role);
                }
                catch (Exception)
                {
                    // Konnte nicht bestimmt werden - dann lieber normal aktivieren als
                    // gar nichts zu tun.
                }
            }

            var eventName = wantsDashboard ? ActivateDashboardEventName : ActivateEventName;
            try
            {
                using var existingActivateEvent = EventWaitHandle.OpenExisting(eventName);
                existingActivateEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Die andere Instanz ist zwischen Mutex-Check und hier bereits beendet -
                // dann gibt es nichts mehr zu aktivieren; sauber beenden statt mit einer
                // eventuell schon verwaisten Sperre weiterzumachen, ein erneuter Klick
                // startet ohnehin eine frische Instanz.
            }

            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateDashboardEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateDashboardEventName);
        new Thread(WaitForActivationRequests) { IsBackground = true }.Start();

        var settingsStore = new SettingsStore();
        OwnSettings settings;
        DeploymentInfo deployment;

        try
        {
            settings = settingsStore.Load();
            deployment = DeploymentInfoStore.Load();
            _license = LicenseReader.Load(deployment.CustomerGroupId, Convert.FromHexString(deployment.LicensePublicKeyHex));
        }
        // FormatException: LicensePublicKeyHex (Issue #56) ist kein gültiger Hex-String -
        // dieselbe Fehlerklasse wie ein beschädigtes deployment.json, nicht separat zu werten.
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or IOException or FormatException)
        {
            // Vorher nur InvalidOperationException (fehlende Datei) abgefangen - eine
            // BESCHÄDIGTE settings.json/deployment.json (z. B. JsonException) flog bis zum
            // DispatcherUnhandledException-Handler durch, der zwar loggt und den Prozess am
            // Leben hält, aber OnStartup an dieser Stelle abbricht - ohne Meldung, ohne
            // Fenster (genau das war "Dashboard öffnet nicht" ohne jede Rückmeldung).
            MessageBox.Show(
                $"Die Konfigurationsdatei ist beschädigt oder unvollständig:{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Bitte HälpMi neu installieren/reparieren.",
                "HälpMi - Installation unvollständig", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Für WaitForActivationRequests gemerkt (siehe Feld-Kommentar oben) - erst NACH
        // erfolgreichem Laden, damit ein nachträgliches Dashboard-Öffnen niemals mit
        // kaputten/fehlenden Settings arbeitet.
        _settingsStore = settingsStore;
        _deployment = deployment;
        _deviceStore = new DeviceStore();

        await EnsureAgentIsRunningAsync();

        // "HälpMi Dashboard"-Startmenüeintrag des Admin-Installers ruft mit diesem Flag auf
        // (siehe installer\HaelpMiCommon.iss.inc [Icons]/[Run]), um ohne Umweg über das
        // normale Einstellungsfenster direkt im Dashboard zu landen. Fallback auf das
        // normale Fenster, falls das Dashboard z. B. wegen eines belegten Edit-Locks nicht
        // geöffnet werden kann - sonst liefe der Prozess sonst fensterlos weiter (kein
        // "letztes Fenster geschlossen"-Ereignis, das den Shutdown auslösen würde).
        if (e.Args.Contains("--open-dashboard") && DashboardAccessGuard.CurrentUserMayOpenDashboard(settings.Role))
        {
            bool opened;
            try
            {
                opened = await OpenAdminDashboardAsync(settingsStore, deployment, _deviceStore);
            }
            catch (Exception ex)
            {
                // Bisher hätte JEDE Ausnahme hier (nicht nur ein sauberes "false") den
                // Fallback unten übersprungen und den Prozess fensterlos enden lassen -
                // exakt die zwei "Dashboard startet nicht"-Vorfälle vom 04.08.2026. Jetzt:
                // loggen, Nutzer informieren, und trotzdem das normale Fenster zeigen.
                CrashLogger.Log(nameof(HaelpMi.Config), "OpenAdminDashboardAsync (--open-dashboard Start)", ex);
                MessageBox.Show(
                    $"Das Dashboard konnte nicht geöffnet werden:{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Öffne stattdessen die normale Konfiguration.",
                    "HälpMi - Dashboard fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
                opened = false;
            }

            if (opened)
            {
                return;
            }
        }

        var context = BuildContext(settingsStore, settings, deployment);
        var configWindow = new ConfigWindow(context);
        MainWindow = configWindow;
        configWindow.Show();
    }

    // Läuft auf einem eigenen Hintergrund-Thread für die gesamte Prozesslaufzeit (kein
    // async/await hier - WaitAny() blockiert absichtlich, es gibt sonst nichts zu tun). Eine
    // zweite gestartete Instanz signalisiert über eines der beiden Events statt selbst ein
    // Fenster zu zeigen (siehe OnStartup oben).
    private void WaitForActivationRequests()
    {
        var handles = new WaitHandle[] { _activateEvent!, _activateDashboardEvent! };
        while (true)
        {
            var signaledIndex = WaitHandle.WaitAny(handles);
            var wantsDashboard = signaledIndex == 1;
            Dispatcher.BeginInvoke(async () =>
            {
                if (wantsDashboard)
                {
                    await ActivateOrOpenDashboardAsync();
                    return;
                }

                // Kann in der kurzen Lücke zwischen Mutex-Erwerb und Fenster-Aufbau (Settings
                // laden, Agent-Ping) noch null sein - dann verpufft dieser eine Aktivierungs-
                // Wunsch einfach, ein erneuter Klick trifft danach ein bereits offenes Fenster.
                if (MainWindow is not { } window)
                {
                    return;
                }

                ActivateWindow(window);
            });
        }
    }

    private static void ActivateWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            ForegroundHelper.ForceForeground(hwnd);
        }
    }

    // Kern des Bugfixes: eine bereits laufende Instanz OHNE Dashboard-Fenster (z. B. nur das
    // normale Konfigurationsfenster offen, siehe ActivateDashboardEventName-Kommentar) muss
    // das Dashboard jetzt NACHTRÄGLICH selbst öffnen können, statt nur zu foregrounden was
    // zufällig schon offen ist - genau das war der Bug ("Dashboard startet gar nicht mehr").
    private async Task ActivateOrOpenDashboardAsync()
    {
        if (_dashboardWindow is { } existing)
        {
            ActivateWindow(existing);
            return;
        }

        if (_settingsStore is null || _deployment is null || _deviceStore is null)
        {
            // Erste Instanz steckt noch vor dem Laden der Settings (sehr kurzes Zeitfenster
            // direkt nach Prozessstart) - kein sinnvoller Zustand zum Dashboard-Öffnen,
            // dann eben nur das MainWindow (falls schon vorhanden) aktivieren.
            if (MainWindow is { } window)
            {
                ActivateWindow(window);
            }

            return;
        }

        try
        {
            await OpenAdminDashboardAsync(_settingsStore, _deployment, _deviceStore);
        }
        catch (Exception ex)
        {
            CrashLogger.Log(nameof(HaelpMi.Config), "ActivateOrOpenDashboardAsync (nachträgliches Öffnen)", ex);
            System.Windows.MessageBox.Show(
                $"Das Dashboard konnte nicht geöffnet werden:{Environment.NewLine}{ex.Message}",
                "HälpMi - Dashboard fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // War früher EnsureAgentIsRunning() mit .GetAwaiter().GetResult() - ein klassischer
    // Sync-over-async-Deadlock auf dem WPF-UI-Thread (siehe Kommentar in IpcClient.cs):
    // die Fortsetzungen in SendAsync wollten auf genau den Thread zurück, der gerade
    // synchron auf sie wartete. Jetzt sauber async/await bis nach oben durchgereicht -
    // OnStartup ist bereits async void.
    private static async Task EnsureAgentIsRunningAsync()
    {
        var ipcClient = new IpcClient();
        var ping = await ipcClient.SendAsync(IpcCommandType.Ping, TimeSpan.FromSeconds(1));
        if (ping.Success)
        {
            return;
        }

        var agentExePath = Path.Combine(AppContext.BaseDirectory, "HaelpMi.Agent.exe");
        if (File.Exists(agentExePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(agentExePath) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // If this fails, the Rebroadcast/SearchAgain/SelfTest buttons will simply
                // report "Hintergrunddienst nicht erreichbar" - editing settings still works.
            }
        }
    }

    private ConfigWindowContext BuildContext(SettingsStore settingsStore, OwnSettings settings, DeploymentInfo deployment)
    {
        var deviceStore = new DeviceStore();
        var ipcClient = new IpcClient();
        var sharedConfigStore = new SharedConfigStore();

        return new ConfigWindowContext
        {
            LoadSettings = settingsStore.Load,
            SaveSettings = settingsStore.Save,
            LoadDevices = deviceStore.Load,
            // Bugfix 08.08.2026 (Fehlerbericht "ich komme gar nicht in die Auswahl" -
            // "Meine Alarme" blieb leer, obwohl im Dashboard ein passendes Profil samt
            // Sender-/Empfänger-Zuordnung für genau dieses Gerät existierte): LoadDevices
            // enthält nur über Boot-Call entdeckte PEERS, nie das eigene Gerät (siehe
            // Kommentar bei AdminDashboardContext.LoadOwnDevice) - RebuildMyAlarms() in
            // ConfigWindow reichte bisher nur LoadDevices() an RecipientResolver weiter.
            // Sobald das eigene Gerät selbst (direkt, über eine Gruppe oder einen Raum) zu
            // seinen eigenen Empfängern zählte, filterte ResolveRecipientsForSenders
            // letzter Schritt (Abgleich der aufgelösten IDs gegen die Geräteliste) das
            // eigene Gerät lautlos wieder heraus - null Empfänger, Profil verschwand komplett
            // aus "Meine Alarme". AdminDashboardContext hatte LoadOwnDevice dafür schon immer,
            // ConfigWindowContext bisher nicht.
            LoadOwnDevice = () =>
            {
                var identity = LiveIdentityFactory.Create(settingsStore.Load(), deployment);
                return new DeviceEntry
                {
                    DeviceId = identity.DeviceId,
                    ComputerName = identity.ComputerName,
                    User = identity.User,
                    RoomName = identity.RoomName,
                    RoomNumber = identity.RoomNumber,
                    Role = identity.Role,
                };
            },
            LoadConfig = sharedConfigStore.LoadOrCreate,
            RequestRebroadcast = async () => (await ipcClient.SendAsync(IpcCommandType.Rebroadcast, TimeSpan.FromSeconds(10))).Success,
            RequestSearchAgain = async () => (await ipcClient.SendAsync(IpcCommandType.SearchAgain, TimeSpan.FromSeconds(10))).Success,
            RequestSelfTest = async () => (await ipcClient.SendAsync(IpcCommandType.SelfTest, TimeSpan.FromSeconds(10))).Success,
            // Issue #10: nur beim installierenden Windows-Nutzer, nicht bei jedem, der auf
            // diesem Admin-Gerät angemeldet ist (Fast User Switching).
            OpenAdminDashboard = DashboardAccessGuard.CurrentUserMayOpenDashboard(settings.Role)
                ? () => OpenAdminDashboardAsync(settingsStore, deployment, deviceStore)
                : null,
        };
    }

    // FR-33: nur Admin-Rollen-Geräte öffnen das Dashboard - nur dann werden ConfigSync/
    // Edit-Lock in diesem (kurzlebigen, on-demand) Prozess überhaupt gestartet.
    //
    // Exklusiv-Edit-Lock (CLAUDE.md, Abschnitt 5): "Admin-Dashboard-Start sendet einen
    // TCP-Call 'will editieren'" - Nutzer-Klarstellung 04.08.2026 (siehe EditScope.cs):
    // nicht mehr einmalig beim Öffnen für einen ganzen Kreis, sondern dynamisch pro
    // ausgewähltem Datensatz (Gruppe oder Alarm-Profil) - AdminDashboardWindow ruft dafür
    // AcquireLock/ReleaseLock über den Context auf, sobald der Admin etwas auswählt bzw.
    // die Auswahl wechselt/das Fenster schließt. Das Dashboard öffnet daher jetzt immer
    // sofort, ohne Warten auf eine Netzwerk-Antwort. Peers sind alle anderen Admin-Geräte
    // in derselben Kunden-Gruppe (keine Kreis-Filterung mehr nötig, da es kein Kreis-
    // Konzept im Dashboard mehr gibt).
    //
    // Rückgabewert (ob tatsächlich ein Fenster geöffnet wurde) wird vom
    // --open-dashboard-Startpfad in OnStartup gebraucht (Fallback-Entscheidung); der
    // Button-Klick in ConfigWindow ignoriert ihn weiterhin einfach (Func&lt;Task&gt; -
    // Task&lt;bool&gt; ist dafür kompatibel, da Task&lt;bool&gt; von Task erbt).
    private Task<bool> OpenAdminDashboardAsync(SettingsStore settingsStore, DeploymentInfo deployment, DeviceStore deviceStore)
    {
        var sharedConfigStore = new SharedConfigStore();
        LiveIdentity IdentityProvider() => LiveIdentityFactory.Create(settingsStore.Load(), deployment);

        _configSync ??= new ConfigSyncService(IdentityProvider, deviceStore.Load);
        _configSync.Start();
        _editLock ??= new EditLockService(IdentityProvider);
        _editLock.Start();

        var ipcClient = new IpcClient();

        // Nutzerwunsch 20.08.2026 ("fliegender Configaustausch" bei jeder gravierenden
        // Änderung): PublishAsync/UndoLastChangeAsync broadcasten zwar schon ihr eigenes,
        // dediziertes Config-Sync-Announce (ConfigSyncService.AnnounceAsync) - der lokal auf
        // demselben Gerät laufende Agent-Prozess ignoriert das aber als eigenes Echo
        // (identische Geräte-ID, siehe ConfigSyncService.HandleAnnounceAsync), lädt seine
        // gecachten Settings also nie automatisch neu und würde bei seinem eigenen nächsten
        // Boot-Call noch die alte ConfigVersion melden. Der schon bestehende Rebroadcast-
        // IPC-Befehl (sonst nur der manuelle "Neu ausstrahlen"-Knopf) lässt den Agent seine
        // Settings neu laden und seinen ganz normalen Discovery-Announce (führt die
        // ConfigVersion ohnehin schon mit) erneut ausstrahlen - andere Geräte lernen die
        // neue Version dadurch vom Admin-Gerät, statt auf dessen nächsten Boot-Call zu
        // warten. Fire-and-forget, damit das sofortige Speichern bei Feld-Blur nicht auf
        // einen zusätzlichen Netzwerk-Roundtrip wartet.
        void NotifyLocalAgentOfConfigChange() => _ = ipcClient.SendAsync(IpcCommandType.Rebroadcast, TimeSpan.FromSeconds(10));

        LicenseCheckResult LoadLicense() => LicenseReader.Load(deployment.CustomerGroupId, Convert.FromHexString(deployment.LicensePublicKeyHex));
        var licenseLimitGuard = new LicenseLimitGuard(IdentityProvider, LoadLicense, deviceStore.Load);

        var context = new AdminDashboardContext
        {
            LoadConfig = sharedConfigStore.LoadOrCreate,
            LoadDevices = deviceStore.Load,
            LoadOwnDevice = () =>
            {
                var identity = IdentityProvider();
                return new DeviceEntry
                {
                    DeviceId = identity.DeviceId,
                    ComputerName = identity.ComputerName,
                    User = identity.User,
                    RoomName = identity.RoomName,
                    RoomNumber = identity.RoomNumber,
                    Role = identity.Role,
                };
            },
            Publish = async (mutate, scopeKind, scopeId, field, oldValue, newValue) =>
            {
                var updated = await _configSync.PublishAsync(mutate, scopeKind, scopeId, field, oldValue, newValue);
                _editLock.TouchActivity(scopeKind, scopeId); // Abschnitt 5: Auto-Freigabe erst nach 10 Min. OHNE Edit-Aktivität
                NotifyLocalAgentOfConfigChange();
                return updated;
            },
            Undo = async (scopeKind, scopeId) =>
            {
                var undone = await _configSync.UndoLastChangeAsync(scopeKind, scopeId);
                if (undone)
                {
                    NotifyLocalAgentOfConfigChange();
                }

                return undone;
            },
            AcquireLock = (scopeKind, scopeId) =>
            {
                var identity = IdentityProvider();
                var peers = deviceStore.Load()
                    .Where(d => d.Role == Role.Admin && d.DeviceId != identity.DeviceId)
                    .ToList();
                return _editLock.TryAcquireAsync(scopeKind, scopeId, peers);
            },
            ReleaseLock = (scopeKind, scopeId) => _editLock.Release(scopeKind, scopeId),
            ExportUserInstaller = ExportUserInstallerAsync,
            ListAvailableUpdateVersions = () => new UpdatePackageCacheStore().ListAvailableVersions(),
            GetLicenseStatus = LoadLicense,
            ImportLicenseKeyText = keyText => LicenseImporter.ImportFromKeyText(keyText, deployment.CustomerGroupId, Convert.FromHexString(deployment.LicensePublicKeyHex)),
            NotifyLicenseRenewed = () => _ = ipcClient.SendAsync(IpcCommandType.LicenseRenewed, TimeSpan.FromSeconds(10)),
            GetDisabledDeviceIds = licenseLimitGuard.GetDisabledDeviceIds,
            GetLicenseSeatLimit = licenseLimitGuard.GetEffectiveUserLimit,
            AcknowledgeLicenseLimitWarning = deviceId =>
            {
                var devices = deviceStore.Load();
                DeviceStore.AcknowledgeLicenseLimitWarning(devices, deviceId);
                deviceStore.Save(devices);
            },
            // Issue #61: lokal setzen und sofort weiter ausstrahlen (SearchAgain löst
            // ohnehin schon einen vollen Discovery-Announce inkl. KnownDevices-Gossip aus,
            // siehe DiscoveryService.AnnounceAsync/HandleSearchAgainRequestAsync).
            SetDeviceLicenseOverride = (deviceId, value) =>
            {
                var devices = deviceStore.Load();
                DeviceStore.SetLicenseOverride(devices, deviceId, value, DateTimeOffset.UtcNow);
                deviceStore.Save(devices);
                _ = ipcClient.SendAsync(IpcCommandType.SearchAgain, TimeSpan.FromSeconds(10));
            },
            DeleteDevice = deviceId =>
            {
                var devices = deviceStore.Load();
                DeviceStore.Remove(devices, deviceId);
                deviceStore.Save(devices);
            },
            RequestSearchAgain = async () => (await ipcClient.SendAsync(IpcCommandType.SearchAgain, TimeSpan.FromSeconds(10))).Success,
            SetDeviceNote = (deviceId, note) =>
            {
                var devices = deviceStore.Load();
                var entry = devices.FirstOrDefault(d => d.DeviceId == deviceId);
                if (entry is not null)
                {
                    entry.Note = note;
                    deviceStore.Save(devices);
                }
            },
        };

        var window = new AdminDashboardWindow(context);
        _dashboardWindow = window;
        window.Closed += (_, _) => _dashboardWindow = null;
        MainWindow = window;
        window.Show();
        return Task.FromResult(true);
    }

    // Nutzerwunsch 04.08.2026: der User-Installer wird nicht mehr live auf dem
    // Kundenrechner per mitgeliefertem Inno-Compiler neu gebaut (Quelle mehrerer ISCC-
    // Bugs - stdout/stderr, fehlende Icon-Datei, verwaiste Prozesse - und ~57 MB
    // unnötiger Compiler-Bausatz im Admin-Installer). Der Install-Creator kompiliert den
    // User-Installer jetzt vorab mit derselben Kunden-Gruppen-ID und bettet die fertige
    // Datei direkt unter {app}\HaelpMi-User-Setup.exe ein (siehe HaelpMiCommon.iss.inc) -
    // "exportieren" ist damit nur noch eine Kopie nach Downloads, kein Kompilieren mehr.
    private static Task<UserInstallerExportResult> ExportUserInstallerAsync()
    {
        var embeddedPath = Path.Combine(AppContext.BaseDirectory, "HaelpMi-User-Setup.exe");
        if (!File.Exists(embeddedPath))
        {
            var message = $"Der eingebettete User-Installer fehlt ({embeddedPath}). Bitte den Admin-Installer neu installieren/reparieren.";
            CrashLogger.Log(nameof(HaelpMi.Config), "ExportUserInstallerAsync (Datei fehlt)", new InvalidOperationException(message));
            return Task.FromResult(new UserInstallerExportResult(false, null, message));
        }

        try
        {
            var downloadsDir = KnownFolders.GetDownloadsFolder();
            Directory.CreateDirectory(downloadsDir);
            var destinationPath = Path.Combine(downloadsDir, "HaelpMi-User-Setup.exe");
            File.Copy(embeddedPath, destinationPath, overwrite: true);

            return Task.FromResult(new UserInstallerExportResult(true, destinationPath, null));
        }
        catch (Exception ex)
        {
            CrashLogger.Log(nameof(HaelpMi.Config), "ExportUserInstallerAsync (unerwartete Ausnahme)", ex);
            return Task.FromResult(new UserInstallerExportResult(false, null, $"Unerwarteter Fehler beim Export: {ex.Message}"));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Locks werden jetzt vom AdminDashboardWindow selbst freigegeben (eigener
        // Closed-Handler, siehe AcquireLock/ReleaseLock im Context) - hier nur noch die
        // Dienste selbst aufräumen.
        _ = _configSync?.DisposeAsync();
        _ = _editLock?.DisposeAsync();

        _activateEvent?.Dispose();
        _activateDashboardEvent?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            // Nur freigeben, wenn diese Instanz sie auch erworben hat - die zweite,
            // sofort wieder beendete Instanz (createdNew == false oben) besitzt sie nie und
            // ReleaseMutex() würde dafür mit SynchronizationLockException abstürzen.
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
