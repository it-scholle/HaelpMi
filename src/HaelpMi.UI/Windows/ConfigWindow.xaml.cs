using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Runtime;
using HaelpMi.Core.Storage;
using HaelpMi.UI.Helpers;
using HaelpMi.UI.ViewModels;

namespace HaelpMi.UI.Windows;

/// <summary>
/// The local settings window (FR-16/FR-18), reworked for Teil 2, Abschnitt 2: Raum,
/// Computername, Kreis und Rolle sind jetzt reine Admin-Dashboard-Daten und werden hier
/// nur noch angezeigt, nicht mehr editiert - siehe <see cref="OwnSettings"/>. Der einzige
/// verbliebene lokal editierbare Wert ist der eingehende Signalton; "Individuell"
/// (bekannte Geräte, FR-18) bleibt unverändert.
/// </summary>
public partial class ConfigWindow : Window
{
    private readonly ConfigWindowContext _context;
    private OwnSettings _settings = null!;
    private ObservableCollection<DeviceEntry> _devices = new();
    private bool _isLoadingGeneral;

    // Testmodus-Toggle (Nutzerwunsch 13.08.2026): _isSyncingTestModeToggle nach demselben
    // Muster wie _isLoadingGeneral oben - verhindert, dass ein programmatisches
    // IsChecked-Zurücksetzen (Re-Sync, Ablauf) als Nutzeraktion in Checked/Unchecked
    // durchschlägt und einen ungewollten Arm/Disarm-IPC-Call auslöst.
    private DispatcherTimer? _testModeCountdownTimer;
    private bool _isSyncingTestModeToggle;

    private sealed record MyAlarmChoice(string Name, string HotkeyText, string RecipientsText);

    // Nutzerwunsch 05.08.2026: automatisch aktualisieren, sobald ein neuer Boot-Call
    // eintrifft/beantwortet wird oder ein Admin die Empfängerkreise ändert - vorher nur bei
    // Fenster-Aktivierung (Activated unten), also unsichtbar, solange das Fenster im
    // Hintergrund/unfokussiert offen blieb.
    private readonly FileChangeWatcher _deviceFileWatcher = new(AppPaths.DevicesFilePath);
    private readonly FileChangeWatcher _configFileWatcher = new(AppPaths.SharedConfigFilePath);

    public ConfigWindow(ConfigWindowContext context)
    {
        InitializeComponent();
        _context = context;

        VersionText.Text = $"v{LiveIdentityFactory.CurrentProgramVersion}";

        foreach (var option in IncomingSoundCatalog.Options)
        {
            IncomingSoundCombo.Items.Add(option);
        }

        LoadGeneralFromDisk();
        ReloadDevicesFromDisk();
        RebuildMyAlarms();
        _ = RefreshTestModeStatusAsync();

        // Re-Sync bei jeder Aktivierung (nicht nur beim Öffnen): der zentrale Baustein gegen
        // "vergessen, ob der Testmodus noch scharf ist" - korrigiert Checkbox+Countdown, falls
        // der Toggle zwischenzeitlich durch einen Hotkey-Trigger verbraucht wurde oder der
        // 2-Minuten-Timeout ablief, während das Fenster im Hintergrund war.
        Activated += (_, _) => { ReloadDevicesFromDisk(); RebuildMyAlarms(); _ = RefreshTestModeStatusAsync(); };
        _deviceFileWatcher.Changed += (_, _) => { ReloadDevicesFromDisk(); RebuildMyAlarms(); };
        _configFileWatcher.Changed += (_, _) => RebuildMyAlarms();
        Closed += (_, _) =>
        {
            _deviceFileWatcher.Dispose();
            _configFileWatcher.Dispose();
            _testModeCountdownTimer?.Stop();
        };
    }

    // Nutzerwunsch 05.08.2026: "was sein eigener Hotkey auslöst und wer benachrichtigt
    // wird" war nirgends im User-Fenster sichtbar - nur ein Profil ist "meins", wenn
    // RecipientResolver für dieses Gerät als Sender (direkt/Raum/Gruppe) überhaupt
    // mindestens einen Empfänger auflöst. Empfänger werden bewusst nur mit Raum(-nummer)
    // angezeigt, kein Nutzername (CLAUDE.md, Datenschutz-Prinzipien) - anders als im Admin-
    // Dashboard, wo gezielt einzelne Personen zugeordnet werden, ist das hier eine reine
    // Übersicht für den Sender selbst.
    private void RebuildMyAlarms()
    {
        var config = _context.LoadConfig();
        // Bugfix 08.08.2026: eigenes Gerät ergänzen (siehe ConfigWindowContext.LoadOwnDevice-
        // Kommentar) - sonst verschwindet ein Profil aus "Meine Alarme", sobald dieses Gerät
        // selbst zu seinen eigenen aufgelösten Empfängern zählt.
        var ownDevice = _context.LoadOwnDevice();
        var devices = _context.LoadDevices().Append(ownDevice).ToList();

        var choices = new List<MyAlarmChoice>();
        foreach (var profile in config.AlarmProfiles)
        {
            var recipients = RecipientResolver.ResolveRecipientsForSender(profile, _settings.DeviceId, _settings.RoomNumber, devices, config.DeviceGroups);
            if (recipients.Count == 0)
            {
                continue;
            }

            var recipientsText = string.Join(", ", recipients
                .OrderBy(d => d.RoomName, StringComparer.CurrentCultureIgnoreCase)
                .Select(d => string.IsNullOrWhiteSpace(d.RoomNumber) ? d.RoomName : $"{d.RoomName} ({d.RoomNumber})"));

            choices.Add(new MyAlarmChoice(profile.Name, profile.Hotkey?.Format() ?? "Kein Tastenkürzel", recipientsText));
        }

        var previouslySelected = MyAlarmsCombo.SelectedItem as MyAlarmChoice;
        MyAlarmsCombo.ItemsSource = choices;
        MyAlarmsCombo.SelectedItem = choices.FirstOrDefault(c => c.Name == previouslySelected?.Name) ?? choices.FirstOrDefault();
        MyAlarmsEmptyText.Visibility = choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MyAlarmsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var choice = MyAlarmsCombo.SelectedItem as MyAlarmChoice;
        MyAlarmHotkeyText.Text = choice?.HotkeyText ?? string.Empty;
        MyAlarmRecipientsText.Text = choice?.RecipientsText ?? string.Empty;
    }

    private void LoadGeneralFromDisk()
    {
        // Unterdrückt die Auto-Speichern-Handler unten, während hier programmatisch befüllt
        // wird (z. B. IncomingSoundCombo.SelectedItem setzen löst sonst
        // IncomingSoundCombo_SelectionChanged aus, obwohl niemand etwas geändert hat).
        _isLoadingGeneral = true;
        try
        {
            _settings = _context.LoadSettings();
            ComputerNameText.Text = _settings.ComputerName;
            RoomNameBox.Text = _settings.RoomName;
            RoomNumberBox.Text = _settings.RoomNumber;
            RoleText.Text = _settings.Role == Role.Admin ? "Admin" : "User";

            var current = IncomingSoundCatalog.Resolve(_settings.IncomingSoundId);
            IncomingSoundCombo.SelectedItem = IncomingSoundCatalog.Options.FirstOrDefault(o => o.Id == current.Id);

            OpenAdminDashboardButton.Visibility = _context.OpenAdminDashboard is not null ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isLoadingGeneral = false;
        }
    }

    // Nutzerwunsch 05.08.2026: "das Windows-Admin-Fenster als Bestätigung" (echtes UAC) ist
    // für später vorgesehen - bis dahin nur ein Hinweis-Popup, einmal pro Fenster-Sitzung,
    // beim ersten Klick in eines der beiden Raum-Felder.
    private bool _roomEditWarningShown;

    private void RoomField_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_roomEditWarningShown)
        {
            return;
        }

        _roomEditWarningShown = true;
        new AdminOnlyNoticeWindow { Owner = this }.ShowDialog();
    }

    private async void RoomField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoadingGeneral)
        {
            return;
        }

        var newRoomName = RoomNameBox.Text.Trim();
        var newRoomNumber = RoomNumberBox.Text.Trim();
        if (newRoomName.Length == 0 || newRoomNumber.Length == 0)
        {
            return; // Pflichtfelder (FR-37) - unvollständige Eingabe wird nicht gespeichert
        }

        if (newRoomName == _settings.RoomName && newRoomNumber == _settings.RoomNumber)
        {
            return;
        }

        _settings.RoomName = newRoomName;
        _settings.RoomNumber = newRoomNumber;
        await SaveGeneralAndRebroadcastAsync();
    }

    private async void IncomingSoundCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingGeneral || IncomingSoundCombo.SelectedItem is not IncomingSoundCatalog.Option selectedSound || selectedSound.Id == _settings.IncomingSoundId)
        {
            return;
        }

        _settings.IncomingSoundId = selectedSound.Id;
        await SaveGeneralAndRebroadcastAsync();
    }

    private async Task SaveGeneralAndRebroadcastAsync()
    {
        try
        {
            _context.SaveSettings(_settings);
            SaveGeneralStatusText.Text = "Gespeichert - wird gemeldet...";

            var ok = await _context.RequestRebroadcast();
            SaveGeneralStatusText.Text = ok
                ? "Gespeichert und an alle Geräte gemeldet."
                : "Gespeichert (Hintergrunddienst gerade nicht erreichbar - wird beim nächsten Start gemeldet).";
        }
        catch (Exception ex)
        {
            SaveGeneralStatusText.Text = "Fehlgeschlagen - siehe Fehlermeldung.";
            ActionErrorHandler.Show(this, "Einstellungen speichern", ex);
        }
    }

    // Nutzerwunsch 04.08.2026 (schon im Admin-Dashboard umgesetzt, jetzt auch hier): "blur
    // soll auch funktionieren, wenn ich nur aus dem Feld klicke, nicht gezwungen aktiv in
    // ein anderes Feld" - siehe AdminDashboardWindow.Window_PreviewMouseDown für die
    // ausführliche Begründung, identisches Vorgehen hier.
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) => Keyboard.Focus(this);

    private async void OpenAdminDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_context.OpenAdminDashboard is null)
        {
            return;
        }

        OpenAdminDashboardButton.IsEnabled = false;
        try
        {
            await _context.OpenAdminDashboard();
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Admin-Dashboard öffnen", ex);
        }
        finally
        {
            OpenAdminDashboardButton.IsEnabled = true;
        }
    }

    private void ReloadDevicesFromDisk()
    {
        // Commit any in-progress Notiz edit first, so a manual/activation-triggered
        // refresh never silently discards a keystroke the user just made.
        DevicesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var devices = _context.LoadDevices();
        var ordered = DeviceStore.OrderForDisplay(devices).ToList();
        _devices = new ObservableCollection<DeviceEntry>(ordered);
        DevicesGrid.ItemsSource = _devices;
    }

    private void PersistDevicesAndReload()
    {
        try
        {
            _context.SaveDevices(_devices.ToList());
            ReloadDevicesFromDisk();
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Geräteliste speichern", ex);
        }
    }

    // Nutzerwunsch 05.08.2026: "Benachrichtigen"-Checkbox entfernt (siehe XAML-Kommentar) -
    // die quittierte "NEU" bisher nebenbei mit (FR-25: "consciously set or left"). Ersatz:
    // eine Zeile anzuklicken zählt jetzt als "gesehen". Dispatcher.BeginInvoke statt einem
    // direkten PersistDevicesAndReload()-Aufruf, aus demselben Grund wie beim Notiz-Feld
    // unten - ein sofortiges ItemsSource-Neuzuweisen mitten aus der DataGrid-eigenen
    // SelectionChanged-Verarbeitung heraus ist derselbe WPF-Reentrancy-Risikofall.
    private void DevicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DevicesGrid.SelectedItem is DeviceEntry { IsNew: true } entry)
        {
            entry.IsNew = false;
            Dispatcher.BeginInvoke(new Action(PersistDevicesAndReload));
        }
    }

    private void DevicesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        if (e.Row.Item is not DeviceEntry entry || e.EditingElement is not TextBox textBox)
        {
            return;
        }

        entry.Note = textBox.Text;
        Dispatcher.BeginInvoke(new Action(PersistDevicesAndReload));
    }

    private async void SearchAgainButton_Click(object sender, RoutedEventArgs e)
    {
        SearchAgainButton.IsEnabled = false;
        try
        {
            await _context.RequestSearchAgain();
            await Task.Delay(500); // brief grace period for replies to arrive before reloading (FR-20)
            ReloadDevicesFromDisk();
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Erneut suchen", ex);
        }
        finally
        {
            SearchAgainButton.IsEnabled = true;
        }
    }

    private async void SelfTestButton_Click(object sender, RoutedEventArgs e)
    {
        SelfTestButton.IsEnabled = false;
        try
        {
            // The Agent performs the actual loopback send/receive and shows its own
            // popup + receipt banner (FR-27) - nothing further to display here.
            await _context.RequestSelfTest();
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Selbsttest", ex);
        }
        finally
        {
            SelfTestButton.IsEnabled = true;
        }
    }

    private async void TestModeToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingTestModeToggle)
        {
            return;
        }

        TestModeToggle.IsEnabled = false;
        try
        {
            var ok = await _context.RequestArmTestMode();
            if (ok)
            {
                StartTestModeCountdown(DateTimeOffset.UtcNow + AppConstants.TestModeTimeout);
            }
            else
            {
                SetTestModeToggleSilently(false);
                TestModeStatusText.Text = "Testmodus konnte nicht aktiviert werden - Hintergrunddienst nicht erreichbar.";
            }
        }
        catch (Exception ex)
        {
            SetTestModeToggleSilently(false);
            ActionErrorHandler.Show(this, "Testmodus aktivieren", ex);
        }
        finally
        {
            TestModeToggle.IsEnabled = true;
        }
    }

    private async void TestModeToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _testModeCountdownTimer?.Stop();
        TestModeStatusText.Text = string.Empty;

        if (_isSyncingTestModeToggle)
        {
            return; // programmatisches Zurücksetzen (Ablauf/Re-Sync), kein Nutzerklick
        }

        // Reiner UX-Komfort für sofortige Rückmeldung, keine Sicherheitsfunktion - der
        // 2-Minuten-Timeout in TestModeArmState greift unabhängig davon, ob dieser
        // IPC-Call ankommt (siehe TestModeArmState-Klassendoku).
        try
        {
            await _context.RequestDisarmTestMode();
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Testmodus deaktivieren", ex);
        }
    }

    private void StartTestModeCountdown(DateTimeOffset expiryUtc)
    {
        _testModeCountdownTimer?.Stop();
        _testModeCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _testModeCountdownTimer.Tick += (_, _) => UpdateTestModeCountdownText(expiryUtc);
        UpdateTestModeCountdownText(expiryUtc);
        _testModeCountdownTimer.Start();
    }

    private void UpdateTestModeCountdownText(DateTimeOffset expiryUtc)
    {
        var remaining = expiryUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            // Reine Anzeige-Aufräumarbeit - der Agent hat sich serverseitig ohnehin schon
            // selbst deaktiviert (TestModeArmState-Zeitstempel-Ablauf), kein IPC-Call nötig.
            _testModeCountdownTimer?.Stop();
            SetTestModeToggleSilently(false);
            TestModeStatusText.Text = string.Empty;
            return;
        }

        TestModeStatusText.Text = $"Testmodus aktiv - noch {remaining:m\\:ss}";
    }

    private void SetTestModeToggleSilently(bool value)
    {
        _isSyncingTestModeToggle = true;
        try
        {
            TestModeToggle.IsChecked = value;
        }
        finally
        {
            _isSyncingTestModeToggle = false;
        }
    }

    // Fragt den tatsächlichen Agent-Zustand ab und korrigiert Checkbox+Countdown danach -
    // der zentrale Baustein gegen "vergessen, ob der Toggle noch an ist" (siehe Aufrufer
    // im Konstruktor/Activated oben).
    private async Task RefreshTestModeStatusAsync()
    {
        TimeSpan? remaining;
        try
        {
            remaining = await _context.RequestTestModeStatus();
        }
        catch
        {
            return; // best effort - der nächste Re-Sync (Activated) korrigiert es ohnehin wieder
        }

        if (remaining is { } r && r > TimeSpan.Zero)
        {
            SetTestModeToggleSilently(true);
            StartTestModeCountdown(DateTimeOffset.UtcNow + r);
        }
        else
        {
            _testModeCountdownTimer?.Stop();
            SetTestModeToggleSilently(false);
            TestModeStatusText.Text = string.Empty;
        }
    }
}
