using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
/// verbliebene lokal editierbare Wert ist der eingehende Signalton; die frühere
/// "Individuell"-Geräteliste (FR-18) ist mit Issue #61 in den Geräte-Tab des
/// Admin-Dashboards umgezogen (dort mit echter Verwaltungsmöglichkeit statt reiner Anzeige).
/// </summary>
public partial class ConfigWindow : Window
{
    private readonly ConfigWindowContext _context;
    private OwnSettings _settings = null!;
    private bool _isLoadingGeneral;

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
        RebuildMyAlarms();

        Activated += (_, _) => RebuildMyAlarms();
        _deviceFileWatcher.Changed += (_, _) => RebuildMyAlarms();
        _configFileWatcher.Changed += (_, _) => RebuildMyAlarms();
        Closed += (_, _) =>
        {
            _deviceFileWatcher.Dispose();
            _configFileWatcher.Dispose();
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
    // ausführliche Begründung (inkl. ComboBox-Ausnahme), identisches Vorgehen hier.
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && WindowFocusHelper.IsWithinComboBox(source))
        {
            return;
        }
        Keyboard.Focus(this);
    }

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

    private async void SearchAgainButton_Click(object sender, RoutedEventArgs e)
    {
        SearchAgainButton.IsEnabled = false;
        try
        {
            await _context.RequestSearchAgain();
            await Task.Delay(500); // brief grace period for replies to arrive before reloading (FR-20)
            RebuildMyAlarms();
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
}
