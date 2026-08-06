using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HaelpMi.UI.Windows;

/// <summary>
/// Kleines, selbst verschwindendes Toast ("wie bei Mac"): schwebt unten rechts von unten
/// nach oben ein, verschwindet nach ein paar Sekunden von selbst wieder mit Fade-out.
/// Genutzt für den "User-Installer exportieren"-Erfolg im Admin-Dashboard.
/// </summary>
public partial class DownloadToastWindow : Window
{
    private readonly string _filePath;
    private readonly DispatcherTimer _autoCloseTimer;

    public DownloadToastWindow(string title, string subText, string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        TitleText.Text = title;
        SubText.Text = subText;

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            AnimateOutAndClose();
        };

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

        _autoCloseTimer.Start();
    }

    private void AnimateOutAndClose()
    {
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_filePath}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // best-effort - kein Absturz, falls Explorer aus irgendeinem Grund nicht startet
        }

        _autoCloseTimer.Stop();
        AnimateOutAndClose();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        AnimateOutAndClose();
    }
}
