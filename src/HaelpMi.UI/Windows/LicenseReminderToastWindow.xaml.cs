using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HaelpMi.Core.Licensing;

namespace HaelpMi.UI.Windows;

/// <summary>
/// Issue #20-Nacharbeit: Systemstart-Erinnerung vor Lizenzablauf (Toast unten rechts,
/// nicht modal - der Agent hat kein Hauptfenster, das dadurch blockiert werden könnte).
/// Nur der Klick auf <see cref="SnoozeButton_Click"/> setzt
/// <see cref="LicenseReminderStateStore.Snooze"/> - Dashboard-öffnen und das X schließen
/// nur dieses eine Fenster, das Popup erscheint beim nächsten Agent-Start unverändert
/// wieder (Nutzervorgabe 02.09.2026).
/// </summary>
public partial class LicenseReminderToastWindow : Window
{
    private readonly Action _openDashboard;

    public LicenseReminderToastWindow(LicenseWarning warning, Action openDashboard)
    {
        InitializeComponent();
        _openDashboard = openDashboard;

        RootBorder.Background = warning.Level == LicenseWarningLevel.ExpiringSoon
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("DangerBrush");
        MessageText.Text = LicenseWarningTextFormatter.Format(warning);

        Loaded += (_, _) => AnimateIn();
    }

    private void AnimateIn()
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 16;

        Left = workArea.Right - Width - margin;
        var targetTop = workArea.Bottom - ActualHeight - margin;
        Top = targetTop + 36; // startet leicht darunter, schwebt beim Einblenden nach oben
        Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slideUp = new DoubleAnimation(Top, targetTop, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280));
        BeginAnimation(TopProperty, slideUp);
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void AnimateOutAndClose()
    {
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        _openDashboard();
        AnimateOutAndClose();
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        LicenseReminderStateStore.Snooze();
        AnimateOutAndClose();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => AnimateOutAndClose();
}
