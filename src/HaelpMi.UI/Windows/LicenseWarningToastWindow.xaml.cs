using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HaelpMi.UI.Windows;

/// <summary>
/// Toast für Lizenz-Warnungen im Config-Prozess (Admin-only, siehe App.xaml.cs) - struktureller
/// Klon von DownloadToastWindow (gleiche Slide-in/Fade-Mechanik), nur ohne "Ordner
/// öffnen"-Aktion und mit warn-/gefahrfarbenem statt grünem Hintergrund.
///
/// !severe (Vorwarnung vor Ablauf): schließt nach 12s von selbst, wie DownloadToastWindow.
/// severe (abgelaufen/keine gültige Lizenz): bewusst KEIN Auto-Close - bleibt sichtbar, bis
/// der Admin ihn manuell schließt. Bleibt trotzdem nicht-modal (.Show(), nicht
/// .ShowDialog()) und blockiert daher nichts anderes im Prozess.
/// </summary>
public partial class LicenseWarningToastWindow : Window
{
    private static readonly SolidColorBrush WarningBackground = new(Color.FromArgb(0xDD, 0xFF, 0x95, 0x00));
    private static readonly SolidColorBrush DangerBackground = new(Color.FromArgb(0xDD, 0xFF, 0x3B, 0x30));

    private readonly DispatcherTimer? _autoCloseTimer;

    public LicenseWarningToastWindow(string title, string body, bool severe)
    {
        InitializeComponent();
        TitleText.Text = title;
        SubText.Text = body;
        ToastBorder.Background = severe ? DangerBackground : WarningBackground;

        if (!severe)
        {
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer!.Stop();
                AnimateOutAndClose();
            };
        }

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

        _autoCloseTimer?.Start();
    }

    private void AnimateOutAndClose()
    {
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer?.Stop();
        AnimateOutAndClose();
    }
}
