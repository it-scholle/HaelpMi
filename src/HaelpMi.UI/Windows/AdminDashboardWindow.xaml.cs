using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Runtime;
using HaelpMi.Core.Storage;
using HaelpMi.UI.Helpers;
using HaelpMi.UI.ViewModels;
using Microsoft.Win32;

namespace HaelpMi.UI.Windows;

/// <summary>
/// Admin-Dashboard-Grundgerüst (Teil 2, Abschnitt 4): Gruppen und Alarm-Profile
/// verwalten. Jede Änderung geht sofort über <see cref="AdminDashboardContext.Publish"/>
/// (Config-Sync-Hot-Reload, kein Speichern-Button je Feld - CLAUDE.md) und landet in der
/// Änderungshistorie mit Undo.
///
/// Nutzerwunsch 04.08.2026: die Empfängerkreis-Zuordnung lebt nicht mehr in einem
/// separaten Popup-Fenster (ehemals RecipientAssignmentWindow, entfernt), sondern direkt
/// im Alarm-Profile-Tab als zweite Spalte neben der Sender-Auswahl. Ein "Sender-Zeile"
/// existiert dabei nicht mehr als eigenständig anzulegendes/löschbares Objekt - sie ergibt
/// sich implizit daraus, ob ein Sender mindestens einen zugeordneten Empfänger hat (leere
/// Zeilen werden beim Entfernen des letzten Empfängers automatisch entfernt).
///
/// Kein "Kreise"-Tab (Nutzer-Klarstellung 04.08.2026, siehe EditScope.cs): Gruppe ist die
/// einzige organisatorische Einheit, ein direkter Zusammenschluss von Geräten.
///
/// Der exklusive Edit-Lock (CLAUDE.md, Abschnitt 5) wird PRO AUSGEWÄHLTEM DATENSATZ
/// erworben - sobald eine Gruppe oder ein Alarm-Profil ausgewählt wird. "Wenn ich Gruppe
/// A1 bearbeite, darf kein anderer diese bearbeiten; wenn ich Alarmprofil B2 bearbeite,
/// darf dort kein anderer rein" - beide Tabs sperren unabhängig voneinander.
/// </summary>
public partial class AdminDashboardWindow : Window
{
    private sealed record DeviceChoice(Guid DeviceId, string DisplayName);

    // IsHighlighted: "wird gerade bearbeitet" (Normalmodus) bzw. "als Übertragen-Ziel
    // gewählt" (Übertragen-Modus) - siehe SenderItemContainerStyle in der XAML.
    private sealed record SenderChoice(EntityRef Ref, string DisplayName, bool IsConfigured, int RecipientCount, bool IsHighlighted);
    private sealed record RecipientChoice(EntityRef Ref, string DisplayName, bool IsAssigned);

    // Bugfix 09.08.2026 ("Benutzerauswahl in Gruppen defekt"): GroupDevicesList lief bisher
    // über die native ListBox-Mehrfachauswahl (SelectedItems) samt SystemColors-Überschreibung
    // fürs Grün - die Auswahl ging bei praktisch jedem ReloadAll() (z. B. nach Speichern eines
    // GANZ ANDEREN Feldes) verloren, weil ItemsSource dabei neu gesetzt wird, ohne
    // SelectedItems aus _selectedGroup.DeviceIds wiederherzustellen. Jetzt exakt dasselbe
    // Prinzip wie die Empfänger-Spalte (RecipientChoice/ToggleListItemStyle): IsAssigned kommt
    // direkt aus der Config, Klick toggelt und schreibt sofort zurück - keine ListBox-eigene
    // Auswahl mehr, die verloren gehen könnte.
    private sealed record GroupDeviceChoice(Guid DeviceId, string DisplayName, bool IsAssigned);

    private readonly AdminDashboardContext _context;
    private SharedConfig _config = null!;
    private List<DeviceChoice> _deviceChoices = new();

    private DeviceGroup? _selectedGroup;
    private AlarmProfile? _selectedProfile;
    private HotkeyDefinition? _capturedProfileHotkey;

    // Welcher Sender (Gerät/Raum/Gruppe) gerade in der rechten Spalte bearbeitet wird -
    // unabhängig davon, ob er schon Empfänger hat oder gerade zum ersten Mal konfiguriert wird.
    private EntityRef? _selectedSenderRef;

    // "Empfängerliste übertragen" (Nutzerwunsch 04.08.2026): während aktiv ist die Sender-
    // Spalte eine Mehrfachauswahl von Zielen statt einer Einzelauswahl zum Bearbeiten.
    private bool _isTransferMode;
    private EntityRef? _transferSourceSenderRef;
    private readonly HashSet<EntityRef> _transferTargets = new();

    // Welcher Lock (falls einer gehalten wird) gerade aktiv ist - für Release beim
    // Auswahlwechsel/Schließen. Getrennt pro Tab, da beide unabhängig voneinander
    // gesperrt werden können (siehe Klassenkommentar).
    private Guid? _heldGroupLockId;
    private Guid? _heldProfileLockId;

    // Updates-Tab (Nutzerwunsch 09.08.2026): ein einziger, fest verdrahteter Datensatz
    // (AppConstants.UpdateRolloutScopeId) statt echter Datensatz-Ids wie bei Gruppe/Profil -
    // deshalb reicht hier ein bool statt eines Guid?, ob der Lock gerade gehalten wird.
    private bool _heldUpdateRolloutLock;

    // Unterdrückt die Auto-Speichern-Handler unten, während LoadGroupDetail/
    // LoadProfileDetail selbst Felder befüllen (z. B. GroupNameBox.Text setzen löst sonst
    // GroupNameBox_LostFocus aus, obwohl der Nutzer nichts geändert hat). GroupDevicesList
    // braucht das nicht mehr (siehe GroupDeviceChoice oben) - Klick dort schreibt direkt,
    // kein SelectionChanged mehr zu unterdrücken.
    private bool _isLoadingDetail;

    // Nutzerwunsch 05.08.2026: automatisch aktualisieren, wenn ein neuer Boot-Call
    // eintrifft/beantwortet wird. Bewusst NICHT die schwere ReloadAll() (die zyklt bei
    // jedem Aufruf Group-/Profile-Edit-Lock neu und würde eine gerade laufende Eingabe
    // durch das kurze IsEnabled=false währenddessen stören) - stattdessen nur die von der
    // Geräteliste abgeleiteten Anzeigen auffrischen, ohne Auswahl/Lock anzufassen.
    private readonly FileChangeWatcher _deviceFileWatcher = new(AppPaths.DevicesFilePath);

    public AdminDashboardWindow(AdminDashboardContext context)
    {
        InitializeComponent();
        _context = context;

        VersionText.Text = $"v{LiveIdentityFactory.CurrentProgramVersion}";

        UpdateUserLabelModeButtons();
        ReloadAll();
        RefreshLicenseBanner();

        _deviceFileWatcher.Changed += (_, _) => RefreshDeviceDerivedViews();
        Closed += (_, _) =>
        {
            ReleaseAllLocks();
            _deviceFileWatcher.Dispose();
        };
    }

    // Nur die Geräte-abgeleiteten Anzeigen auffrischen (Gruppen-Geräteliste inkl.
    // Zusammenfassung, Sender-/Empfänger-Spalten) - Gruppen-/Profil-AUSWAHL und deren Locks
    // bleiben unangetastet, damit ein neu entdecktes Gerät im Hintergrund nicht mitten in
    // einer laufenden Bearbeitung den Fokus wegreißt.
    private void RefreshDeviceDerivedViews()
    {
        var ownDevice = _context.LoadOwnDevice();
        _deviceChoices = _context.LoadDevices().Append(ownDevice)
            .Select(d => new DeviceChoice(d.DeviceId, BuildDeviceDisplayName(d, isOwnDevice: d.DeviceId == ownDevice.DeviceId)))
            .OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        RebuildGroupDevicesPanel();

        if (_selectedGroup is not null)
        {
            UpdateGroupSummaries();
        }

        if (_selectedProfile is not null)
        {
            RebuildSenderPanels();
            RebuildRecipientPanels();
        }
    }

    // Nutzerwunsch 04.08.2026: "blur soll auch funktionieren, wenn ich nur aus dem Feld
    // klicke, nicht gezwungen aktiv in ein anderes Feld" - WPF feuert LostFocus nur, wenn
    // die Tastatur-Fokus tatsächlich zu einem ANDEREN fokussierbaren Element wandert. Ein
    // Klick auf leeren Hintergrund (Grid/TextBlock, nicht fokussierbar) bewegt den Fokus
    // gar nicht erst, also blieb das Feld fokussiert und LostFocus feuerte nie.
    //
    // Ein früherer Versuch filterte per Ancestor-Typ (TextBox/ComboBox/ListBoxItem/...) und
    // löste nur aus, wenn KEINER davon in der Klickkette vorkam - blieb aber weiterhin
    // unzuverlässig (z. B. Klicks innerhalb der ListBox-Chrome/ScrollViewer, die keiner der
    // gelisteten Typen sind, aber trotzdem nicht immer griffen). Robuster: bei JEDEM Klick
    // im Fenster bedingungslos zuerst auf das Fenster selbst fokussieren (Preview-Events
    // tunneln von der Wurzel nach unten, laufen also VOR der eigentlichen Klick-Behandlung
    // ab) - falls das tatsächliche Ziel selbst fokussierbar ist (TextBox, ComboBox, ...),
    // übernimmt die normale WPF-Klick-Logik direkt danach ohnehin wieder dessen Fokus.
    // Nutzer-Repro 25.08.2026 (Issue #1): ein Klick, der die ComboBox öffnet, feuert dieses
    // Handler ebenfalls - Keyboard.Focus(this) mitten im Öffnen des Popups riss der ComboBox
    // dabei den Fokus weg, wodurch der anschließende Klick auf ein Popup-Element nicht mehr
    // als Auswahl zählte, sondern wie ein Klick außerhalb des Popups behandelt wurde.
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && WindowFocusHelper.IsWithinComboBox(source))
        {
            return;
        }
        Keyboard.Focus(this);
    }

    // Bugfix 07.08.2026 (Fehlerbericht "Sender deselektiert sich, wenn ich einen Empfänger
    // auswähle"): GroupsList/ProfileCombo setzen ihr ItemsSource bei jedem Reload auf null
    // und dann neu (siehe ReloadProfileCombo() unten - nötig, damit die Liste die neuen
    // Objektinstanzen nach dem Config-Reload zeigt). Das kurzzeitige ItemsSource=null lässt
    // WPF SelectedItem zwischenzeitlich auf null UND Items.Count auf 0 fallen - genau der
    // Fall, den die "Items.Count > 0"-Wächter in ProfileCombo_SelectionChanged/
    // GroupsList_SelectionChanged eigentlich abfangen sollten, der bei Count==0 aber selbst
    // NICHT greift. Jeder Reload (z. B. durchs Zuordnen eines Empfängers, siehe
    // AssignRecipientAsync) wurde dadurch von den SelectionChanged-Handlern fälschlich als
    // "Nutzer hat wirklich auf 'keine Auswahl' gewechselt" interpretiert -
    // _selectedSenderRef/_heldProfileLockId/_heldGroupLockId gingen verloren.
    //
    // Erster Fix-Versuch unterdrückte SelectionChanged während JEDES ReloadAll() komplett -
    // das brach dabei das allererste Laden beim Fensteröffnen (nichts vorher ausgewählt):
    // dort MUSS der echte Handler laufen (Edit-Lock anfordern, Felder befüllen, Panel
    // freischalten - das passiert nur in dessen "andere Auswahl"-Zweig). Deshalb jetzt nur
    // unterdrücken, wenn VORHER schon etwas ausgewählt war (reiner "gleiche fachliche
    // Auswahl, neue Objektinstanz nach Reload"-Fall) - beim allerersten Laden bleibt der
    // normale Ablauf unangetastet.
    private bool _suppressGroupSelectionChanged;
    private bool _suppressProfileSelectionChanged;

    private void ReloadAll()
    {
        _config = _context.LoadConfig();
        // Nutzer-Frage 04.08.2026: LoadDevices() sind nur über Boot-Call entdeckte Peers,
        // das eigene Gerät fehlt dort immer - ohne LoadOwnDevice() könnte der Admin sich
        // selbst nie einer Gruppe zuordnen oder als Sender/Empfänger auswählen.
        var ownDevice = _context.LoadOwnDevice();
        _deviceChoices = _context.LoadDevices().Append(ownDevice)
            .Select(d => new DeviceChoice(d.DeviceId, BuildDeviceDisplayName(d, isOwnDevice: d.DeviceId == ownDevice.DeviceId)))
            .OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var groupSelectionId = _selectedGroup?.Id;
        var profileSelectionId = _selectedProfile?.Id;

        _suppressGroupSelectionChanged = _selectedGroup is not null;
        _suppressProfileSelectionChanged = _selectedProfile is not null;
        try
        {
            GroupsList.ItemsSource = null;
            GroupsList.ItemsSource = _config.DeviceGroups;
            GroupsList.SelectedItem = _config.DeviceGroups.FirstOrDefault(g => g.Id == groupSelectionId);

            ReloadProfileCombo();
        }
        finally
        {
            _suppressGroupSelectionChanged = false;
            _suppressProfileSelectionChanged = false;
        }

        // Objektinstanzen direkt aus den Controls nachziehen (dieselbe fachliche Auswahl,
        // aber neu erzeugte Objekte nach dem Reload) - _selectedSenderRef/Locks/etc. bleiben
        // unangetastet, das ist der ganze Zweck der Unterdrückung oben. War vorher NICHTS
        // ausgewählt, hat der (nicht unterdrückte) echte Handler das bereits selbst erledigt -
        // hier dann nicht nochmal überschreiben.
        if (groupSelectionId is not null)
        {
            _selectedGroup = GroupsList.SelectedItem as DeviceGroup;
        }
        if (profileSelectionId is not null)
        {
            _selectedProfile = ProfileCombo.SelectedItem as AlarmProfile;
        }

        RebuildGroupDevicesPanel();
        RebuildSenderPanels();
        RebuildRecipientPanels();
        if (_selectedGroup is not null)
        {
            UpdateGroupSummaries();
        }
    }

    // Nutzerwunsch 04.08.2026: alphabetisch sortiert, standardmäßig immer der erste
    // verfügbare Eintrag gewählt (nicht "keine Auswahl") - auch nach dem Löschen eines
    // Profils oder beim allerersten Öffnen. Wählt ProfileCombo dasselbe Profil wie vorher
    // (gleiche Id, aber neue Objektinstanz nach dem Config-Reload) erneut aus, wird das im
    // SelectionChanged-Handler erkannt und der Edit-Lock NICHT unnötig neu angefordert.
    private void ReloadProfileCombo()
    {
        var sorted = _config.AlarmProfiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        ProfileCombo.ItemsSource = null;
        ProfileCombo.ItemsSource = sorted;

        var target = sorted.FirstOrDefault(p => p.Id == _selectedProfile?.Id) ?? sorted.FirstOrDefault();
        ProfileCombo.SelectedItem = target;
    }

    private static string BuildDeviceDisplayName(DeviceEntry device, bool isOwnDevice)
    {
        var room = string.IsNullOrWhiteSpace(device.RoomName) ? "kein Raum" : device.RoomName;
        var suffix = isOwnDevice ? ", dieses Gerät" : "";
        return $"{device.ComputerName} - {room} ({device.User}{suffix})";
    }

    private void ReleaseAllLocks()
    {
        if (_heldGroupLockId is { } groupId)
        {
            _context.ReleaseLock(EditScopeKind.Group, groupId);
            _heldGroupLockId = null;
        }

        if (_heldProfileLockId is { } profileId)
        {
            _context.ReleaseLock(EditScopeKind.Profile, profileId);
            _heldProfileLockId = null;
        }

        if (_heldUpdateRolloutLock)
        {
            _context.ReleaseLock(EditScopeKind.UpdateRollout, AppConstants.UpdateRolloutScopeId);
            _heldUpdateRolloutLock = false;
        }
    }

    // --------------------------------------------------- Lizenz (Issue #19/#20/#51) ---

    // Live neu ausgewertet statt einmalig beim Öffnen gecacht (siehe Kommentar an
    // AdminDashboardContext.GetLicenseStatus) - ein Import muss sofort sichtbar werden.
    private void RefreshLicenseBanner()
    {
        var checkResult = _context.GetLicenseStatus();
        var warning = LicenseWarningEvaluator.Evaluate(checkResult, DateTime.UtcNow);

        if (warning.Level == LicenseWarningLevel.None)
        {
            LicenseWarningBanner.Visibility = Visibility.Collapsed;
            return;
        }

        LicenseWarningBanner.Background = warning.Level == LicenseWarningLevel.ExpiringSoon
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("DangerBrush");
        LicenseWarningText.Text = warning.Level switch
        {
            LicenseWarningLevel.Missing => "Keine Lizenz gefunden. Bitte eine gültige Lizenzdatei einspielen.",
            LicenseWarningLevel.Invalid => "Lizenz ungültig (beschädigt, manipuliert oder für eine andere Installation ausgestellt). Bitte eine gültige Lizenzdatei einspielen.",
            LicenseWarningLevel.Expired => $"Lizenz seit {-warning.DaysRemaining} Tag(en) abgelaufen. Bitte eine neue Lizenz einspielen.",
            LicenseWarningLevel.ExpiringSoon => $"Lizenz läuft in {warning.DaysRemaining} Tag(en) ab. Bitte rechtzeitig eine neue Lizenz einspielen.",
            _ => string.Empty,
        };
        LicenseWarningBanner.Visibility = Visibility.Visible;
    }

    // Issue #51 (Lizenz-Import im Admin-Dashboard), verdrahtet direkt im #20-Banner: baut
    // auf #19 auf (LicenseImporter prüft über LicenseReader, bevor irgendetwas übernommen
    // wird - eine ungültige Auswahl überschreibt eine bestehende gültige Lizenz nie).
    private void ImportLicenseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Lizenzdatei auswählen",
            Filter = "Lizenzdatei (*.json)|*.json|Alle Dateien (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = _context.ImportLicenseFile(dialog.FileName);
        if (!result.Success)
        {
            var reason = result.CheckResult.Status == LicenseStatus.Missing
                ? "Die Datei konnte nicht gelesen werden."
                : "Die Datei ist keine gültige Lizenz für diese Installation (Signatur oder Kundengruppe passt nicht).";
            MessageBox.Show(this, reason, "HälpMi - Lizenz einspielen fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshLicenseBanner();
    }

    // ------------------------------------------------------------- User-Installer-Export ---

    private async void ExportUserInstallerButton_Click(object sender, RoutedEventArgs e)
    {
        ExportUserInstallerButton.IsEnabled = false;
        ExportStatusText.Text = "Wird erstellt - kann einen Moment dauern...";
        try
        {
            var result = await _context.ExportUserInstaller();
            if (result.Success && result.OutputFilePath is not null)
            {
                ExportStatusText.Text = string.Empty;
                var toast = new DownloadToastWindow(
                    "User-Installer exportiert",
                    Path.GetFileName(result.OutputFilePath) + " liegt im Downloads-Ordner.",
                    result.OutputFilePath);
                toast.Show();
            }
            else
            {
                ExportStatusText.Text = "Export fehlgeschlagen.";
                MessageBox.Show(result.Error ?? "Unbekannter Fehler beim Export.", "HälpMi - Export fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            ExportUserInstallerButton.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------------ Gruppen ---

    // Nutzerwunsch 05.08.2026: reassigning ListBox.ItemsSource (in ReloadAll(), z. B. nach
    // JEDER Feldänderung) leert die Auswahl kurzzeitig, BEVOR SelectedItem gleich danach
    // wieder gesetzt wird - das feuert SelectionChanged zwischenzeitlich mit
    // SelectedItem=null, obwohl kein Nutzer je "nichts" angeklickt hat. Ohne diese Prüfung
    // löste JEDE Speicherung (z. B. ein Gerät in der Gruppe an-/abwählen) den vollen "andere
    // Gruppe gewählt"-Zweig aus: Lock freigeben, Panel leeren - die eigentliche Auswahl ging
    // dabei verloren. Ein Wechsel auf "wirklich nichts ausgewählt" kommt nur vor, wenn die
    // Liste selbst leer ist (letzte Gruppe gelöscht) - das bleibt weiterhin erlaubt.
    private async void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGroupSelectionChanged)
        {
            return;
        }

        if (GroupsList.SelectedItem is null && GroupsList.Items.Count > 0)
        {
            return;
        }

        if (_heldGroupLockId is { } previousId)
        {
            _context.ReleaseLock(EditScopeKind.Group, previousId);
            _heldGroupLockId = null;
        }

        _selectedGroup = GroupsList.SelectedItem as DeviceGroup;
        GroupDetailPanel.IsEnabled = false;

        if (_selectedGroup is null)
        {
            LoadGroupDetail();
            return;
        }

        var groupId = _selectedGroup.Id;
        GroupStatusText.Text = "Wird zur Bearbeitung reserviert...";
        try
        {
            var result = await _context.AcquireLock(EditScopeKind.Group, groupId);
            if (GroupsList.SelectedItem is not DeviceGroup current || current.Id != groupId)
            {
                // Auswahl hat sich während der Netzwerk-Anfrage schon wieder geändert -
                // Lock (falls doch noch gewährt) sofort wieder freigeben, nichts anzeigen.
                if (result.Outcome == EditLockAcquireOutcome.Granted)
                {
                    _context.ReleaseLock(EditScopeKind.Group, groupId);
                }
                return;
            }

            if (result.Outcome != EditLockAcquireOutcome.Granted)
            {
                GroupStatusText.Text = result.Outcome == EditLockAcquireOutcome.DeniedByHolder
                    ? $"Wird gerade von {result.HolderComputerName} ({result.HolderUser}) bearbeitet - nur Ansicht."
                    : "Konnte nicht exklusiv reserviert werden - bitte erneut auswählen.";
                LoadGroupDetail();
                return;
            }

            _heldGroupLockId = groupId;
            GroupStatusText.Text = string.Empty;
            LoadGroupDetail();
            GroupDetailPanel.IsEnabled = true;
        }
        catch (Exception ex)
        {
            GroupStatusText.Text = "Reservierung fehlgeschlagen - siehe Fehlermeldung.";
            ActionErrorHandler.Show(this, "Gruppe zur Bearbeitung reservieren", ex);
        }
    }

    private void LoadGroupDetail()
    {
        _isLoadingDetail = true;
        try
        {
            RebuildGroupDevicesPanel();

            if (_selectedGroup is null)
            {
                GroupNameBox.Text = string.Empty;
                GroupNameBox.IsEnabled = true;
                DeleteGroupButton.IsEnabled = true;
                GroupDevicesList.IsEnabled = true;
                GroupRoomsSummaryText.Text = string.Empty;
                GroupUsersSummaryText.Text = string.Empty;
                return;
            }

            // "Alle" (Nutzerwunsch 09.08.2026): fest verdrahtete Default-Gruppe, nicht
            // umbenennbar/löschbar/manuell bearbeitbar - ihre Mitgliedschaft ergibt sich
            // automatisch aus allen aktuell bekannten Geräten (siehe RebuildGroupDevicesPanel/
            // RecipientResolver), nicht aus einer im Dashboard gepflegten Auswahl.
            var isBuiltInAll = _selectedGroup.IsBuiltInAllDevicesGroup;
            GroupNameBox.Text = _selectedGroup.Name;
            GroupNameBox.IsEnabled = !isBuiltInAll;
            DeleteGroupButton.IsEnabled = !isBuiltInAll;
            GroupDevicesList.IsEnabled = !isBuiltInAll;
            UpdateGroupSummaries();
        }
        finally
        {
            _isLoadingDetail = false;
        }
    }

    // Nutzerwunsch 04.08.2026 / Bugfix 09.08.2026: dieselbe Aufbau-Logik wie
    // RebuildRecipientPanels - IsAssigned kommt direkt und ausschließlich aus
    // _selectedGroup.DeviceIds (Quelle der Wahrheit), nie aus einer ListBox-eigenen
    // Auswahl, die bei einem ItemsSource-Reset verloren gehen könnte. Ausnahme "Alle"
    // (Nutzerwunsch 09.08.2026): DeviceIds bleibt bei ihr leer, jedes Gerät gilt trotzdem
    // als Mitglied - siehe DeviceGroup.IsBuiltInAllDevicesGroup.
    private void RebuildGroupDevicesPanel()
    {
        if (_selectedGroup is null)
        {
            GroupDevicesList.ItemsSource = null;
            return;
        }

        var isBuiltInAll = _selectedGroup.IsBuiltInAllDevicesGroup;
        GroupDevicesList.ItemsSource = _deviceChoices
            .Select(d => new GroupDeviceChoice(d.DeviceId, d.DisplayName, isBuiltInAll || _selectedGroup.DeviceIds.Contains(d.DeviceId)))
            .ToList();
    }

    // Nutzerwunsch 04.08.2026: automatisch akkumulierte Anzeige, welche Räume/Nutzer in
    // der Gruppe stecken - aus den Mitgliedsgeräten abgeleitet, nicht separat gepflegt.
    private void UpdateGroupSummaries()
    {
        if (_selectedGroup is null)
        {
            return;
        }

        var members = _selectedGroup.IsBuiltInAllDevicesGroup
            ? _context.LoadDevices().Append(_context.LoadOwnDevice()).ToList()
            : _context.LoadDevices().Where(d => _selectedGroup.DeviceIds.Contains(d.DeviceId)).ToList();
        var rooms = members.Select(d => string.IsNullOrWhiteSpace(d.RoomName) ? "kein Raum" : d.RoomName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(r => r, StringComparer.CurrentCultureIgnoreCase).ToList();
        var users = members.Select(d => d.User).Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(u => u, StringComparer.CurrentCultureIgnoreCase).ToList();

        GroupRoomsSummaryText.Text = rooms.Count > 0 ? string.Join(", ", rooms) : "(keine Geräte in dieser Gruppe)";
        GroupUsersSummaryText.Text = users.Count > 0 ? string.Join(", ", users) : "(keine Geräte in dieser Gruppe)";
    }

    private async void NewGroupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var group = new DeviceGroup { Name = "Neue Gruppe" };
            await PublishAsync(cfg => { cfg.DeviceGroups.Add(group); return cfg; }, EditScopeKind.Group, group.Id, "Gruppe angelegt", null, group.Name);
            ReloadAll();
            GroupsList.SelectedItem = _config.DeviceGroups.FirstOrDefault(g => g.Id == group.Id);
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Gruppe anlegen", ex);
        }
    }

    private void GroupNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoadingDetail || _selectedGroup is null || _selectedGroup.IsBuiltInAllDevicesGroup)
        {
            return; // "Alle" (Nutzerwunsch 09.08.2026): nicht umbenennbar, siehe LoadGroupDetail
        }

        var newName = GroupNameBox.Text.Trim();
        if (newName.Length == 0 || newName == _selectedGroup.Name)
        {
            return;
        }

        SaveGroupFieldAsync("Name", _selectedGroup.Name, newName, cfg =>
        {
            cfg.DeviceGroups.First(g => g.Id == _selectedGroup.Id).Name = newName;
        });
    }

    // Bugfix 09.08.2026: Klick toggelt direkt Mitgliedschaft, grün = Mitglied - dasselbe
    // Prinzip wie RecipientList_PreviewMouseLeftButtonUp bei der Empfänger-Spalte, statt der
    // vorherigen (defekten) nativen ListBox-Mehrfachauswahl.
    //
    // Bugfix 09.08.2026, zweiter Versuch ("Auswahl immer noch defekt - erster Nutzer geht,
    // danach leert sich die Liste wieder"): newDeviceIds wurde bisher AUSSERHALB der
    // mutate-Closure aus _selectedGroup.DeviceIds berechnet - dem lokalen UI-Stand, der
    // noch nicht den gerade erst gespeicherten vorherigen Klick enthält, solange dessen
    // PublishAsync (Netzwerk-Roundtrip) noch läuft. AssignRecipientAsync/UnassignRecipientAsync
    // machen es richtig: die Änderung wird INNERHALB der Closure auf dem gerade frisch von
    // der Platte geladenen cfg berechnet (siehe PublishAsync/ConfigSyncService - lädt jedes
    // Mal neu). GroupDetailPanel bleibt zusätzlich während des Speicherns gesperrt (wie schon
    // beim Lock-Erwerb), damit ein zweiter Klick nicht mehr auf denselben veralteten
    // UI-Stand trifft, bevor der erste Roundtrip überhaupt zurück ist.
    private void GroupDevicesList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectedGroup is null || _selectedGroup.IsBuiltInAllDevicesGroup || FindDataContext<GroupDeviceChoice>(e.OriginalSource) is not { } choice)
        {
            return; // "Alle" (Nutzerwunsch 09.08.2026): Mitgliedschaft automatisch, nicht manuell togglebar
        }

        var groupId = _selectedGroup.Id;
        var wasAssigned = choice.IsAssigned;
        var oldCount = _selectedGroup.DeviceIds.Count;
        var newCount = wasAssigned ? oldCount - 1 : oldCount + 1;

        SaveGroupFieldAsync("Enthaltene Geräte", $"{oldCount} Gerät(e)", $"{newCount} Gerät(e)", cfg =>
        {
            var group = cfg.DeviceGroups.First(g => g.Id == groupId);
            if (wasAssigned)
            {
                group.DeviceIds.Remove(choice.DeviceId);
            }
            else if (!group.DeviceIds.Contains(choice.DeviceId))
            {
                group.DeviceIds.Add(choice.DeviceId);
            }
        });
    }

    private async void SaveGroupFieldAsync(string fieldName, string? oldValue, string? newValue, Action<SharedConfig> mutate)
    {
        if (_selectedGroup is null)
        {
            return;
        }

        var groupId = _selectedGroup.Id;
        GroupDetailPanel.IsEnabled = false;
        try
        {
            await PublishAsync(cfg => { mutate(cfg); return cfg; }, EditScopeKind.Group, groupId, $"Gruppe - {fieldName}", oldValue, newValue);
            ReloadAll();
            UpdateGroupSummaries();
            GroupStatusText.Text = "Gespeichert und an alle Geräte verteilt.";
        }
        catch (Exception ex)
        {
            GroupStatusText.Text = "Fehlgeschlagen - siehe Fehlermeldung.";
            ActionErrorHandler.Show(this, $"Gruppe - {fieldName} speichern", ex);
        }
        finally
        {
            GroupDetailPanel.IsEnabled = _selectedGroup is not null;
        }
    }

    private async void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGroup is null || _selectedGroup.IsBuiltInAllDevicesGroup)
        {
            return; // "Alle" (Nutzerwunsch 09.08.2026): nicht löschbar, Button ist dafür bereits deaktiviert (LoadGroupDetail)
        }

        // Nutzerwunsch 04.08.2026: kein natives MessageBox-Bestätigungsfenster mehr ("noch
        // hässlich alt Windows") - Rückgängig deckt ein Versehen ab.
        try
        {
            var groupId = _selectedGroup.Id;
            var name = _selectedGroup.Name;
            await PublishAsync(cfg => { cfg.DeviceGroups.RemoveAll(g => g.Id == groupId); return cfg; }, EditScopeKind.Group, groupId, "Gruppe gelöscht", name, null);
            _selectedGroup = null;
            if (_heldGroupLockId == groupId)
            {
                _context.ReleaseLock(EditScopeKind.Group, groupId);
                _heldGroupLockId = null;
            }
            ReloadAll();
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Gruppe löschen", ex);
        }
    }

    private async void UndoGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGroup is null)
        {
            return;
        }

        try
        {
            var ok = await _context.Undo(EditScopeKind.Group, _selectedGroup.Id);
            GroupStatusText.Text = ok ? "Letzte Änderung rückgängig gemacht." : "Keine Änderung zum Rückgängigmachen vorhanden.";
            ReloadAll();
            UpdateGroupSummaries();
        }
        catch (Exception ex)
        {
            GroupStatusText.Text = "Fehlgeschlagen - siehe Fehlermeldung.";
            ActionErrorHandler.Show(this, "Gruppen-Änderung rückgängig machen", ex);
        }
    }

    // ------------------------------------------------------------- Alarm-Profile ---

    private async void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileSelectionChanged)
        {
            return;
        }

        var newProfile = ProfileCombo.SelectedItem as AlarmProfile;

        // Nutzerwunsch 05.08.2026: reassigning ComboBox.ItemsSource (in ReloadProfileCombo(),
        // nach JEDER Speicherung - z. B. einen Empfänger an-/abwählen) leert SelectedItem
        // kurzzeitig, BEVOR es gleich danach wieder gesetzt wird - das feuert
        // SelectionChanged zwischenzeitlich mit null, obwohl kein Nutzer "nichts" angeklickt
        // hat. Ohne diese Prüfung ging dabei die gerade gewählte Sender-Auswahl
        // (_selectedSenderRef) sofort wieder verloren, sobald irgendein Empfänger getoggelt
        // wurde. Ein Wechsel auf "wirklich kein Profil" kommt nur vor, wenn die Liste selbst
        // leer ist (letztes Profil gelöscht).
        if (newProfile is null && ProfileCombo.Items.Count > 0)
        {
            return;
        }

        // ReloadAll() nach jedem Speichern wählt dasselbe Profil (gleiche Id, aber neue
        // Objektinstanz nach dem Config-Reload) erneut aus - das feuert SelectionChanged,
        // obwohl der Nutzer nichts wirklich gewechselt hat. Ohne diese Prüfung würde jedes
        // einzelne Feld-Speichern den Lock unnötig freigeben und neu anfordern UND die
        // gerade in Bearbeitung befindliche Sender-Auswahl (_selectedSenderRef) verlieren.
        if (newProfile is not null && newProfile.Id == _selectedProfile?.Id)
        {
            _selectedProfile = newProfile;
            RebuildSenderPanels();
            RebuildRecipientPanels();
            return;
        }

        if (_heldProfileLockId is { } previousId)
        {
            _context.ReleaseLock(EditScopeKind.Profile, previousId);
            _heldProfileLockId = null;
        }

        _selectedProfile = newProfile;
        _selectedSenderRef = null;
        ExitTransferModeSilently();
        ProfileFieldsPanel.IsEnabled = false;
        SenderRecipientPanel.IsEnabled = false;

        if (_selectedProfile is null)
        {
            LoadProfileDetail();
            return;
        }

        var profileId = _selectedProfile.Id;
        ProfileStatusText.Text = "Wird zur Bearbeitung reserviert...";
        try
        {
            var result = await _context.AcquireLock(EditScopeKind.Profile, profileId);
            if (ProfileCombo.SelectedItem is not AlarmProfile current || current.Id != profileId)
            {
                if (result.Outcome == EditLockAcquireOutcome.Granted)
                {
                    _context.ReleaseLock(EditScopeKind.Profile, profileId);
                }
                return;
            }

            if (result.Outcome != EditLockAcquireOutcome.Granted)
            {
                ProfileStatusText.Text = result.Outcome == EditLockAcquireOutcome.DeniedByHolder
                    ? $"Wird gerade von {result.HolderComputerName} ({result.HolderUser}) bearbeitet - nur Ansicht."
                    : "Konnte nicht exklusiv reserviert werden - bitte erneut auswählen.";
                LoadProfileDetail();
                return;
            }

            _heldProfileLockId = profileId;
            ProfileStatusText.Text = string.Empty;
            LoadProfileDetail();
            ProfileFieldsPanel.IsEnabled = true;
            SenderRecipientPanel.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ProfileStatusText.Text = "Reservierung fehlgeschlagen - siehe Fehlermeldung.";
            ActionErrorHandler.Show(this, "Alarm-Profil zur Bearbeitung reservieren", ex);
        }
    }

    private void LoadProfileDetail()
    {
        _isLoadingDetail = true;
        try
        {
            if (_selectedProfile is null)
            {
                ProfileNameBox.Text = string.Empty;
                ProfileTextBox.Text = string.Empty;
                _capturedProfileHotkey = null;
                ProfileHotkeyBox.Text = "Kein Tastenkürzel";
                ProfileThresholdBox.Text = string.Empty;
                RebuildSenderPanels();
                RebuildRecipientPanels();
                return;
            }

            ProfileNameBox.Text = _selectedProfile.Name;
            ProfileTextBox.Text = _selectedProfile.Text;
            _capturedProfileHotkey = _selectedProfile.Hotkey;
            ProfileHotkeyBox.Text = _capturedProfileHotkey?.Format() ?? "Kein Tastenkürzel";
            ProfileThresholdBox.Text = _selectedProfile.ResponseThreshold.ToString();
            RebuildSenderPanels();
            RebuildRecipientPanels();
        }
        finally
        {
            _isLoadingDetail = false;
        }
    }

    // Escape löste bisher "Tastenkürzel löschen" aus - das verhindert, Escape selbst als
    // Hotkey zu benutzen. Entf/Rücktaste übernehmen jetzt das Löschen, Escape ist ein
    // normal erfassbarer Key wie jeder andere (Nutzerwunsch 04.08.2026).
    private void ProfileHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key is Key.Delete or Key.Back)
        {
            _capturedProfileHotkey = null;
            ProfileHotkeyBox.Text = "Kein Tastenkürzel";
            SaveProfileHotkeyAsync();
            return;
        }

        var hotkey = HotkeyInputHelper.TryCapture(e);
        if (hotkey is null)
        {
            return;
        }

        _capturedProfileHotkey = hotkey;
        ProfileHotkeyBox.Text = hotkey.Format();
        SaveProfileHotkeyAsync();
    }

    private async void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = new AlarmProfile { Name = "Neues Profil", Text = "Bitte sofort kommen!" };
            await PublishAsync(cfg => { cfg.AlarmProfiles.Add(profile); return cfg; }, EditScopeKind.Profile, profile.Id, "Alarm-Profil angelegt", null, profile.Name);

            // Bugfix 06.08.2026 (Fehlerbericht "neues Profil nicht direkt editierbar, Name
            // etc. bleibt leer"): _selectedProfile hier VORHER auf das neue Profil zu setzen
            // ließ ReloadProfileCombo() (ruft ReloadAll() auf) dieselbe Id gleich wieder
            // auswählen - ProfileCombo_SelectionChanged erkannte das dann als "nur neu
            // instanziiert, Nutzer hat nichts wirklich gewechselt" (siehe Kommentar dort) und
            // übersprang Lock-Anforderung, Feld-Befüllung (LoadProfileDetail) UND das
            // Freischalten von ProfileFieldsPanel/SenderRecipientPanel - das neue Profil blieb
            // leer und nicht editierbar stehen. Stattdessen _selectedProfile unverändert lassen,
            // ReloadAll() aufrufen (wählt dadurch zunächst die bisherige Auswahl unverändert
            // erneut, harmlos), und erst danach das neue Profil ganz regulär per SelectedItem
            // auswählen - das durchläuft den echten "anderes Profil gewählt"-Zweig inklusive
            // Lock/LoadProfileDetail/Freischalten, genau wie ein normaler Nutzerklick.
            ReloadAll();
            ProfileCombo.SelectedItem = _config.AlarmProfiles.FirstOrDefault(p => p.Id == profile.Id);
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Alarm-Profil anlegen", ex);
        }
    }

    private void ProfileNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoadingDetail || _selectedProfile is null)
        {
            return;
        }

        var newName = ProfileNameBox.Text.Trim();
        if (newName.Length == 0 || newName == _selectedProfile.Name)
        {
            return;
        }

        SaveProfileFieldAsync("Name", _selectedProfile.Name, newName, cfg =>
        {
            cfg.AlarmProfiles.First(p => p.Id == _selectedProfile.Id).Name = newName;
        });
    }

    private void ProfileTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoadingDetail || _selectedProfile is null)
        {
            return;
        }

        var newText = ProfileTextBox.Text;
        if (newText.Trim().Length == 0 || newText == _selectedProfile.Text)
        {
            return;
        }

        SaveProfileFieldAsync("Anzeigetext", _selectedProfile.Text, newText, cfg =>
        {
            cfg.AlarmProfiles.First(p => p.Id == _selectedProfile.Id).Text = newText;
        });
    }

    private void ProfileThresholdBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoadingDetail || _selectedProfile is null)
        {
            return;
        }

        if (!int.TryParse(ProfileThresholdBox.Text.Trim(), out var newThreshold) || newThreshold < 1)
        {
            ProfileStatusText.Text = "Schwellwert muss eine ganze Zahl ≥ 1 sein - nicht gespeichert.";
            return;
        }

        if (newThreshold == _selectedProfile.ResponseThreshold)
        {
            return;
        }

        SaveProfileFieldAsync("Schwellwert", _selectedProfile.ResponseThreshold.ToString(), newThreshold.ToString(), cfg =>
        {
            cfg.AlarmProfiles.First(p => p.Id == _selectedProfile.Id).ResponseThreshold = newThreshold;
        });
    }

    private async void SaveProfileHotkeyAsync()
    {
        if (_isLoadingDetail || _selectedProfile is null)
        {
            return;
        }

        var oldFormat = _selectedProfile.Hotkey?.Format() ?? "keins";
        var newFormat = _capturedProfileHotkey?.Format() ?? "keins";
        if (oldFormat == newFormat)
        {
            return;
        }

        var capturedHotkey = _capturedProfileHotkey;
        await SaveProfileFieldAsyncCore("Tastenkürzel", oldFormat, newFormat, cfg =>
        {
            cfg.AlarmProfiles.First(p => p.Id == _selectedProfile.Id).Hotkey = capturedHotkey;
        });
    }

    private async void SaveProfileFieldAsync(string fieldName, string? oldValue, string? newValue, Action<SharedConfig> mutate) =>
        await SaveProfileFieldAsyncCore(fieldName, oldValue, newValue, mutate);

    private async Task SaveProfileFieldAsyncCore(string fieldName, string? oldValue, string? newValue, Action<SharedConfig> mutate)
    {
        if (_selectedProfile is null)
        {
            return;
        }

        try
        {
            await PublishAsync(cfg => { mutate(cfg); return cfg; }, EditScopeKind.Profile, _selectedProfile.Id, $"Alarm-Profil - {fieldName}", oldValue, newValue);
            ReloadAll();
            ProfileStatusText.Text = "Gespeichert und an alle Geräte verteilt.";
        }
        catch (Exception ex)
        {
            ProfileStatusText.Text = "Fehlgeschlagen - siehe Fehlermeldung.";
            ActionErrorHandler.Show(this, $"Alarm-Profil - {fieldName} speichern", ex);
        }
    }

    // Nutzerwunsch 04.08.2026: kein natives MessageBox-Bestätigungsfenster mehr ("noch
    // hässlich alt Windows") - stattdessen ein "-" direkt neben dem "+" im Dropdown, löscht
    // sofort; Rückgängig deckt ein Versehen ab (siehe UndoProfileButton_Click).
    private async void DeleteProfileIconButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null)
        {
            return;
        }

        try
        {
            var profileId = _selectedProfile.Id;
            var name = _selectedProfile.Name;
            await PublishAsync(cfg => { cfg.AlarmProfiles.RemoveAll(p => p.Id == profileId); return cfg; }, EditScopeKind.Profile, profileId, "Alarm-Profil gelöscht", name, null);
            _selectedProfile = null;
            if (_heldProfileLockId == profileId)
            {
                _context.ReleaseLock(EditScopeKind.Profile, profileId);
                _heldProfileLockId = null;
            }
            ReloadAll();
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Alarm-Profil löschen", ex);
        }
    }

    private async void UndoProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null)
        {
            return;
        }

        try
        {
            var ok = await _context.Undo(EditScopeKind.Profile, _selectedProfile.Id);
            ProfileStatusText.Text = ok ? "Letzte Änderung rückgängig gemacht." : "Keine Änderung zum Rückgängigmachen vorhanden.";
            ReloadAll();
        }
        catch (Exception ex)
        {
            ProfileStatusText.Text = "Fehlgeschlagen - siehe Fehlermeldung.";
            ActionErrorHandler.Show(this, "Alarm-Profil-Änderung rückgängig machen", ex);
        }
    }

    // ------------------------------------------------ Sender-/Empfänger-Zuordnung ---
    // Nutzerwunsch 04.08.2026: zwei Spalten statt drei. Spalte 1 (Sender) zeigt IMMER alle
    // möglichen Sender, ein Klick WÄHLT einen zur Bearbeitung aus (kein Toggle) - fett +
    // blauer Zähler-Badge markiert Sender, die bereits Empfänger haben, sortiert an den
    // Anfang ihrer jeweiligen Kategorie. Spalte 2 (Empfänger) zeigt alle möglichen
    // Empfänger für den in Spalte 1 gewählten Sender, grün = zugeordnet, Klick toggelt.

    private void RebuildSenderPanels()
    {
        if (_selectedProfile is null)
        {
            SenderUsersList.ItemsSource = null;
            SenderRoomsList.ItemsSource = null;
            SenderGroupsList.ItemsSource = null;
            TransferRecipientsButton.IsEnabled = false;
            return;
        }

        var ownDevice = _context.LoadOwnDevice();
        var devices = _context.LoadDevices().Append(ownDevice).ToList();
        var groups = _config.DeviceGroups;
        var assignments = _selectedProfile.RecipientAssignments;

        SenderChoice Build(EntityRef entityRef, string displayName)
        {
            var row = assignments.FirstOrDefault(a => a.Sender == entityRef);
            var count = row is null ? 0 : RecipientResolver.CountDistinctRecipientDevices(row.Recipients, devices, groups);
            var isHighlighted = _isTransferMode ? _transferTargets.Contains(entityRef) : entityRef == _selectedSenderRef;
            return new SenderChoice(entityRef, displayName, count > 0, count, isHighlighted);
        }

        // Nutzerwunsch 07.08.2026: "Empfängerliste übertragen" darf nur bedienbar sein, wenn
        // ein Sender MIT mindestens einem Empfänger ausgewählt ist - sonst gäbe es nur eine
        // leere Liste zu übertragen. Dieselbe Zähl-Logik wie Build() oben (IsConfigured),
        // hier zentral für den ausgewählten Sender statt pro Listeneintrag.
        var selectedSenderRow = _selectedSenderRef is { } selectedRef ? assignments.FirstOrDefault(a => a.Sender == selectedRef) : null;
        TransferRecipientsButton.IsEnabled = selectedSenderRow is not null
            && RecipientResolver.CountDistinctRecipientDevices(selectedSenderRow.Recipients, devices, groups) > 0;

        List<SenderChoice> SortConfiguredFirst(IEnumerable<SenderChoice> choices) =>
            choices.OrderByDescending(c => c.IsConfigured).ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();

        SenderUsersList.ItemsSource = SortConfiguredFirst(devices.Select(d =>
            Build(new EntityRef(EntityKind.Device, d.DeviceId), BuildUserLabel(d, isOwnDevice: d.DeviceId == ownDevice.DeviceId))));

        SenderRoomsList.ItemsSource = SortConfiguredFirst(devices
            .Where(d => !string.IsNullOrWhiteSpace(d.RoomNumber))
            .GroupBy(d => d.RoomNumber)
            .Select(g => Build(EntityRef.ForRoom(g.Key), BuildRoomLabel(g))));

        SenderGroupsList.ItemsSource = SortConfiguredFirst(groups.Select(g =>
            Build(new EntityRef(EntityKind.Group, g.Id), $"Gruppe: {g.Name}")));
    }

    private void RebuildRecipientPanels()
    {
        if (_selectedProfile is null || _selectedSenderRef is not { } senderRef)
        {
            RecipientUsersList.ItemsSource = null;
            RecipientRoomsList.ItemsSource = null;
            RecipientGroupsList.ItemsSource = null;
            return;
        }

        var assigned = _selectedProfile.RecipientAssignments.FirstOrDefault(a => a.Sender == senderRef)?.Recipients ?? new List<EntityRef>();
        var ownDevice = _context.LoadOwnDevice();
        var devices = _context.LoadDevices().Append(ownDevice).ToList();
        var groups = _config.DeviceGroups;

        RecipientUsersList.ItemsSource = devices
            .Select(d => new RecipientChoice(new EntityRef(EntityKind.Device, d.DeviceId), BuildUserLabel(d, isOwnDevice: d.DeviceId == ownDevice.DeviceId), assigned.Contains(new EntityRef(EntityKind.Device, d.DeviceId))))
            .ToList();

        RecipientRoomsList.ItemsSource = devices
            .Where(d => !string.IsNullOrWhiteSpace(d.RoomNumber))
            .GroupBy(d => d.RoomNumber)
            .Select(g => new RecipientChoice(EntityRef.ForRoom(g.Key), BuildRoomLabel(g), assigned.Contains(EntityRef.ForRoom(g.Key))))
            .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        RecipientGroupsList.ItemsSource = groups
            .Select(g => new RecipientChoice(new EntityRef(EntityKind.Group, g.Id), $"Gruppe: {g.Name}", assigned.Contains(new EntityRef(EntityKind.Group, g.Id))))
            .ToList();
    }

    // Nutzerwunsch 07.08.2026: "läuft immer, egal welcher Nutzer angemeldet ist" (Teil 2) -
    // der reine Username ist damit kein zuverlässiger Bezeichner mehr (derselbe Rechner kann
    // je nach Anmeldung unterschiedliche Namen zeigen, ein Kiosk-/Gemeinschaftsgerät sogar
    // ständig wechselnde) - inkonsequent, wenn der Admin dann nur den Nutzer sieht, nie das
    // Gerät. Umschaltbar statt eine feste Kombination zu erzwingen, Standard bleibt "Nutzer"
    // (unverändertes Verhalten, CLAUDE.md Datenschutz-Prinzipien: möglichst wenig auf einen
    // Blick, der Admin kann bei Bedarf mehr einblenden).
    private enum UserLabelMode { User, Device, Both }

    private UserLabelMode _userLabelMode = UserLabelMode.User;

    private void UserLabelModeButton_Click(object sender, RoutedEventArgs e)
    {
        _userLabelMode = sender switch
        {
            _ when ReferenceEquals(sender, UserLabelModeDeviceButton) => UserLabelMode.Device,
            _ when ReferenceEquals(sender, UserLabelModeBothButton) => UserLabelMode.Both,
            _ => UserLabelMode.User,
        };

        UpdateUserLabelModeButtons();
        RebuildSenderPanels();
        RebuildRecipientPanels();
    }

    private void UpdateUserLabelModeButtons()
    {
        void SetActive(System.Windows.Controls.Button button, bool active)
        {
            button.Background = active ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("SecondaryButtonBrush");
            button.Foreground = active ? Brushes.White : (Brush)FindResource("TextPrimaryBrush");
        }

        SetActive(UserLabelModeUserButton, _userLabelMode == UserLabelMode.User);
        SetActive(UserLabelModeDeviceButton, _userLabelMode == UserLabelMode.Device);
        SetActive(UserLabelModeBothButton, _userLabelMode == UserLabelMode.Both);
    }

    // CLAUDE.md, Datenschutz-Prinzipien: Raum prominent, Username klein - hier aber ist der
    // Nutzer selbst der gesuchte Zweck der Liste (der Admin muss ihn gezielt zuordnen
    // können), daher voran, mit dem Raum zur Wiedererkennung dahinter.
    private string BuildUserLabel(DeviceEntry device, bool isOwnDevice)
    {
        var user = string.IsNullOrWhiteSpace(device.User) ? device.ComputerName : device.User;
        var identity = _userLabelMode switch
        {
            UserLabelMode.Device => device.ComputerName,
            UserLabelMode.Both => $"{user} / {device.ComputerName}",
            _ => user,
        };
        var room = string.IsNullOrWhiteSpace(device.RoomName) ? "kein Raum" : device.RoomName;
        var suffix = isOwnDevice ? ", dieses Gerät" : "";
        return $"{identity} ({room}{suffix})";
    }

    private static string BuildRoomLabel(IGrouping<string, DeviceEntry> roomGroup)
    {
        var roomName = roomGroup.Select(d => d.RoomName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        var label = string.IsNullOrWhiteSpace(roomName) ? $"Raum {roomGroup.Key}" : $"{roomName} ({roomGroup.Key})";
        var count = roomGroup.Count();
        return $"{label} - {count} {(count == 1 ? "Gerät" : "Geräte")}";
    }

    // Ein Handler für alle drei Sender-Listen (Nutzer/Räume/Gruppen). Normalmodus: wählt
    // den Sender zur Bearbeitung aus. Übertragen-Modus: toggelt ihn als Übertragen-Ziel.
    private void SenderList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindDataContext<SenderChoice>(e.OriginalSource) is not { } choice)
        {
            return;
        }

        if (_isTransferMode)
        {
            if (choice.Ref == _transferSourceSenderRef)
            {
                return; // die Quelle kann nicht gleichzeitig ihr eigenes Ziel sein
            }

            if (!_transferTargets.Remove(choice.Ref))
            {
                _transferTargets.Add(choice.Ref);
            }
            RebuildSenderPanels();
            return;
        }

        _selectedSenderRef = choice.Ref;
        RebuildSenderPanels();
        RebuildRecipientPanels();
    }

    private async void RecipientList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindDataContext<RecipientChoice>(e.OriginalSource) is not { } choice)
        {
            return;
        }

        if (choice.IsAssigned)
        {
            await UnassignRecipientAsync(choice);
        }
        else
        {
            await AssignRecipientAsync(choice);
        }
    }

    private static T? FindDataContext<T>(object originalSource) where T : class
    {
        var element = originalSource as DependencyObject;
        while (element is not null and not ListBoxItem)
        {
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return (element as ListBoxItem)?.DataContext as T;
    }

    private async Task AssignRecipientAsync(RecipientChoice recipient)
    {
        if (_selectedProfile is null || _selectedSenderRef is not { } senderRef)
        {
            return;
        }

        try
        {
            var profileId = _selectedProfile.Id;
            await PublishAsync(cfg =>
            {
                var profile = cfg.AlarmProfiles.First(p => p.Id == profileId);
                var row = profile.RecipientAssignments.FirstOrDefault(r => r.Sender == senderRef);
                if (row is null)
                {
                    row = new RecipientAssignment { Sender = senderRef, Recipients = new List<EntityRef>() };
                    profile.RecipientAssignments.Add(row);
                }
                if (!row.Recipients.Contains(recipient.Ref))
                {
                    row.Recipients.Add(recipient.Ref);
                }
                return cfg;
            }, EditScopeKind.Profile, profileId, "Empfänger zugeordnet", null, recipient.DisplayName);

            ReloadAll();
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Empfänger zuordnen", ex);
        }
    }

    private async Task UnassignRecipientAsync(RecipientChoice recipient)
    {
        if (_selectedProfile is null || _selectedSenderRef is not { } senderRef)
        {
            return;
        }

        try
        {
            var profileId = _selectedProfile.Id;
            await PublishAsync(cfg =>
            {
                var profile = cfg.AlarmProfiles.First(p => p.Id == profileId);
                var row = profile.RecipientAssignments.FirstOrDefault(r => r.Sender == senderRef);
                if (row is not null)
                {
                    row.Recipients.RemoveAll(r => r == recipient.Ref);
                    if (row.Recipients.Count == 0)
                    {
                        // Keine explizite "Sender-Zeile löschen"-Aktion mehr im UI - eine
                        // Zeile ohne Empfänger ist implizit keine aktive Zeile mehr.
                        profile.RecipientAssignments.Remove(row);
                    }
                }
                return cfg;
            }, EditScopeKind.Profile, profileId, "Empfänger entfernt", recipient.DisplayName, null);

            ReloadAll();
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Empfänger entfernen", ex);
        }
    }

    // -------------------------------------------------- "Empfängerliste übertragen" ---

    private void TransferRecipientsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSenderRef is null)
        {
            ProfileStatusText.Text = "Bitte zuerst links einen Sender auswählen, dessen Empfängerliste übertragen werden soll.";
            return;
        }

        _isTransferMode = true;
        _transferSourceSenderRef = _selectedSenderRef;
        _transferTargets.Clear();

        TransferRecipientsButton.Visibility = Visibility.Collapsed;
        TransferModeButtons.Visibility = Visibility.Visible;
        ProfileFieldsPanel.IsEnabled = false;
        RecipientColumnPanel.IsEnabled = false;

        RebuildSenderPanels();
    }

    private async void TransferApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null || _transferSourceSenderRef is not { } sourceRef || _transferTargets.Count == 0)
        {
            ExitTransferModeSilently();
            RebuildSenderPanels();
            return;
        }

        try
        {
            var profileId = _selectedProfile.Id;
            var sourceRecipients = _selectedProfile.RecipientAssignments.FirstOrDefault(r => r.Sender == sourceRef)?.Recipients.ToList() ?? new List<EntityRef>();
            var targets = _transferTargets.ToList();

            await PublishAsync(cfg =>
            {
                var profile = cfg.AlarmProfiles.First(p => p.Id == profileId);
                foreach (var targetRef in targets)
                {
                    var row = profile.RecipientAssignments.FirstOrDefault(r => r.Sender == targetRef);
                    if (row is null)
                    {
                        row = new RecipientAssignment { Sender = targetRef, Recipients = new List<EntityRef>() };
                        profile.RecipientAssignments.Add(row);
                    }
                    row.Recipients = sourceRecipients.ToList();
                }
                return cfg;
            }, EditScopeKind.Profile, profileId, "Empfängerliste übertragen", null, $"{targets.Count} Sender");

            ProfileStatusText.Text = $"Empfängerliste auf {targets.Count} Sender übertragen und verteilt.";
        }
        catch (Exception ex)
        {
            ActionErrorHandler.Show(this, "Empfängerliste übertragen", ex);
        }
        finally
        {
            ExitTransferModeSilently();
            ReloadAll();
        }
    }

    private void TransferCancelButton_Click(object sender, RoutedEventArgs e)
    {
        ExitTransferModeSilently();
        RebuildSenderPanels();
    }

    // "Silently" = ohne UI-Reset erneut auszulösen (z. B. beim Profilwechsel mitten im
    // Übertragen-Modus) - RebuildSenderPanels()/ReloadAll() übernehmen danach die eigentliche Anzeige.
    private void ExitTransferModeSilently()
    {
        _isTransferMode = false;
        _transferSourceSenderRef = null;
        _transferTargets.Clear();
        TransferRecipientsButton.Visibility = Visibility.Visible;
        TransferModeButtons.Visibility = Visibility.Collapsed;
        ProfileFieldsPanel.IsEnabled = _selectedProfile is not null;
        RecipientColumnPanel.IsEnabled = true;
    }

    private Task<SharedConfig> PublishAsync(Func<SharedConfig, SharedConfig> mutate, EditScopeKind scopeKind, Guid scopeId, string fieldPath, string? oldValue, string? newValue) =>
        _context.Publish(mutate, scopeKind, scopeId, fieldPath, oldValue, newValue);

    // --------------------------------------------------------------------- Updates ---
    // Nutzerwunsch 09.08.2026: gleiches Lock-bei-Auswahl-Prinzip wie Gruppe/Alarm-Profil
    // (CLAUDE.md, Abschnitt 5), nur pro Tab statt pro Listenzeile - es gibt hier immer genau
    // einen Datensatz (AppConstants.UpdateRolloutScopeId).
    //
    // TabControl.SelectionChanged ist ein bubbelndes RoutedEvent (Selector-Basisklasse) -
    // GroupsList/ProfileCombo lösen ihr eigenes SelectionChanged aus, das bis hierher
    // durchreicht. e.Source zeigt dabei weiterhin auf das ursprünglich auslösende Element,
    // nicht auf MainTabControl - ohne diese Prüfung würde jede Gruppen-/Profilauswahl
    // fälschlich als "Updates-Tab verlassen" interpretiert und den Lock unnötig freigeben.
    private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Absicherung gegen die automatische "erster Tab ausgewählt"-Auslösung während
        // InitializeComponent() selbst (anders als GroupsList/ProfileCombo bekommt
        // MainTabControl seine TabItems direkt aus der XAML, nicht erst später per
        // ItemsSource in ReloadAll() - ein Auto-Select kann daher schon VOR der
        // _context-Zuweisung im Konstruktor feuern).
        if (_context is null || e.Source != MainTabControl)
        {
            return;
        }

        var isUpdatesTabNow = ReferenceEquals(MainTabControl.SelectedItem, UpdatesTabItem);
        if (_heldUpdateRolloutLock && !isUpdatesTabNow)
        {
            _context.ReleaseLock(EditScopeKind.UpdateRollout, AppConstants.UpdateRolloutScopeId);
            _heldUpdateRolloutLock = false;
        }

        if (!isUpdatesTabNow)
        {
            return;
        }

        UpdatesPanel.IsEnabled = false;
        UpdatesStatusText.Text = "Wird zur Bearbeitung reserviert...";
        try
        {
            var result = await _context.AcquireLock(EditScopeKind.UpdateRollout, AppConstants.UpdateRolloutScopeId);
            if (!ReferenceEquals(MainTabControl.SelectedItem, UpdatesTabItem))
            {
                // Tab schon wieder gewechselt, während die Netzwerk-Anfrage lief - Lock
                // (falls doch noch gewährt) sofort wieder freigeben, nichts anzeigen.
                if (result.Outcome == EditLockAcquireOutcome.Granted)
                {
                    _context.ReleaseLock(EditScopeKind.UpdateRollout, AppConstants.UpdateRolloutScopeId);
                }
                return;
            }

            if (result.Outcome != EditLockAcquireOutcome.Granted)
            {
                UpdatesStatusText.Text = result.Outcome == EditLockAcquireOutcome.DeniedByHolder
                    ? $"Wird gerade von {result.HolderComputerName} ({result.HolderUser}) bearbeitet - nur Ansicht."
                    : "Konnte nicht exklusiv reserviert werden - bitte Tab erneut wählen.";
                LoadUpdatesTab();
                return;
            }

            _heldUpdateRolloutLock = true;
            UpdatesStatusText.Text = string.Empty;
            LoadUpdatesTab();
            UpdatesPanel.IsEnabled = true;
        }
        catch (Exception ex)
        {
            UpdatesStatusText.Text = "Reservierung fehlgeschlagen - siehe Fehlermeldung.";
            ActionErrorHandler.Show(this, "Updates-Tab zur Bearbeitung reservieren", ex);
        }
    }

    private void LoadUpdatesTab()
    {
        _isLoadingDetail = true;
        try
        {
            OwnVersionText.Text = $"v{LiveIdentityFactory.CurrentProgramVersion}";

            var available = _context.ListAvailableUpdateVersions().OrderByDescending(v => v).ToList();
            AvailableVersionsText.Text = available.Count > 0
                ? string.Join(", ", available)
                : "(keine Version im Cache - siehe BUILD-UND-INSTALLATION.md, Schritt 1b, oder wartet auf einen ersten P2P-Pull)";
            AvailableVersionsCombo.ItemsSource = available;
            AvailableVersionsCombo.SelectedItem = available.FirstOrDefault();

            var rollout = _config.UpdateRollout;
            ApprovedVersionText.Text = string.IsNullOrWhiteSpace(rollout.ApprovedVersion)
                ? "Kein aktiver Rollout."
                : $"Version {rollout.ApprovedVersion}, freigegeben für {rollout.ApprovedDeviceQuota} von {_deviceChoices.Count} Geräten.";
            QuotaBox.Text = rollout.ApprovedDeviceQuota.ToString();
        }
        finally
        {
            _isLoadingDetail = false;
        }
    }

    private async void ReleaseVersionButton_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableVersionsCombo.SelectedItem is not string version || string.IsNullOrWhiteSpace(version))
        {
            UpdatesStatusText.Text = "Bitte zuerst eine Version aus dem Cache auswählen.";
            return;
        }

        await SaveUpdateRolloutFieldAsync("Version freigegeben (Stufe 1)", _config.UpdateRollout.ApprovedVersion, version, cfg =>
        {
            cfg.UpdateRollout.ApprovedVersion = version;
            cfg.UpdateRollout.ApprovedDeviceQuota = 1;
        });
    }

    private void QuotaBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoadingDetail)
        {
            return;
        }

        if (!int.TryParse(QuotaBox.Text.Trim(), out var newQuota) || newQuota < 0)
        {
            UpdatesStatusText.Text = "Kontingent muss eine ganze Zahl ≥ 0 sein - nicht gespeichert.";
            return;
        }

        if (newQuota == _config.UpdateRollout.ApprovedDeviceQuota)
        {
            return;
        }

        _ = SaveUpdateRolloutFieldAsync("Freigabekontingent", _config.UpdateRollout.ApprovedDeviceQuota.ToString(), newQuota.ToString(), cfg =>
        {
            cfg.UpdateRollout.ApprovedDeviceQuota = newQuota;
        });
    }

    // Nutzerwunsch: "Admin gibt Freigabestufen frei (z. B. 1 -> 2 -> 4 -> 8 Geräte)"
    // (Anweisungen/claude-code-prompt-teil2-admin-update.md, Abschnitt 11) - Verdopplung
    // gedeckelt auf die Gesamtzahl bekannter Geräte, ein größeres Kontingent hätte ohnehin
    // keine zusätzliche Wirkung (UpdateOrchestrator.IsMyTurn).
    private async void NextStageButton_Click(object sender, RoutedEventArgs e)
    {
        var current = _config.UpdateRollout.ApprovedDeviceQuota;
        var deviceCount = Math.Max(1, _deviceChoices.Count);
        var next = Math.Min(current <= 0 ? 1 : current * 2, deviceCount);
        if (next == current)
        {
            return;
        }

        await SaveUpdateRolloutFieldAsync("Freigabekontingent - nächste Stufe", current.ToString(), next.ToString(), cfg =>
        {
            cfg.UpdateRollout.ApprovedDeviceQuota = next;
        });
    }

    private async void StopRolloutButton_Click(object sender, RoutedEventArgs e)
    {
        var oldVersion = _config.UpdateRollout.ApprovedVersion;
        if (string.IsNullOrWhiteSpace(oldVersion))
        {
            return; // schon kein aktiver Rollout
        }

        await SaveUpdateRolloutFieldAsync("Rollout gestoppt", oldVersion, null, cfg =>
        {
            cfg.UpdateRollout.ApprovedVersion = null;
            cfg.UpdateRollout.ApprovedDeviceQuota = 0;
        });
    }

    private async Task SaveUpdateRolloutFieldAsync(string fieldName, string? oldValue, string? newValue, Action<SharedConfig> mutate)
    {
        try
        {
            await PublishAsync(cfg => { mutate(cfg); return cfg; }, EditScopeKind.UpdateRollout, AppConstants.UpdateRolloutScopeId, $"Update-Rollout - {fieldName}", oldValue, newValue);
            ReloadAll();
            LoadUpdatesTab();
            UpdatesStatusText.Text = "Gespeichert und an alle Geräte verteilt.";
        }
        catch (Exception ex)
        {
            UpdatesStatusText.Text = "Fehlgeschlagen - siehe Fehlermeldung.";
            ActionErrorHandler.Show(this, $"Update-Rollout - {fieldName} speichern", ex);
        }
    }

    private async void UndoUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ok = await _context.Undo(EditScopeKind.UpdateRollout, AppConstants.UpdateRolloutScopeId);
            UpdatesStatusText.Text = ok ? "Letzte Änderung rückgängig gemacht." : "Keine Änderung zum Rückgängigmachen vorhanden.";
            ReloadAll();
            LoadUpdatesTab();
        }
        catch (Exception ex)
        {
            UpdatesStatusText.Text = "Fehlgeschlagen - siehe Fehlermeldung.";
            ActionErrorHandler.Show(this, "Update-Rollout-Änderung rückgängig machen", ex);
        }
    }
}
