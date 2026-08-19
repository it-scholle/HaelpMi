using System.Windows;

namespace HaelpMi.UpdateBootstrapper;

/// <summary>
/// Flaw 14 ("Updater öffnet bei Fehlschlag Konsole statt App-UI"): ersetzt die bisher
/// einzige Rückmeldung bei einem Install/Test/Swap-Fehlschlag (reiner Konsolentext, siehe
/// Program.cs) durch ein zur restlichen App stilkonsistentes WPF-Fenster
/// (ModernStyles.xaml aus HaelpMi.UI, gleiches Muster wie AlarmPopupWindow/
/// LicenseWarningToastWindow). Bewusst NUR im Fehlerfall aufgerufen - im Erfolgsfall bleibt
/// der bisherige Konsolentext unverändert, kein zusätzliches Fenster nötig (CLAUDE.md
/// "kein Over-Engineering": UpdateBootstrapper ist weiterhin primär ein Konsolen-Tool, das
/// Fenster kommt nur dazu, wo die Konsole allein nicht mehr reicht).
///
/// Läuft ohne eigene System.Windows.Application-Instanz (Program.Main ist weiterhin ein
/// reiner [STAThread]-Konsolen-Entry-Point, kein WPF-App-Objekt) - ShowDialog() pumpt dafür
/// intern eine eigene Dispatcher-Frame, Styles werden direkt in Window.Resources gemergt
/// statt über Application.Current.
/// </summary>
public partial class UpdateFailureWindow : Window
{
    public UpdateFailureWindow(string step, string message, string? hint)
    {
        InitializeComponent();
        StepText.Text = $"Schritt: {step}";
        MessageText.Text = message;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            HintText.Text = hint;
            HintText.Visibility = Visibility.Visible;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
