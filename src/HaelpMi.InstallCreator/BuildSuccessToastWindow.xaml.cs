using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Eigenständige Kopie von HaelpMi.UI.Windows.DownloadToastWindow (siehe Kommentar in
/// HaelpMi.InstallCreator.csproj: bewusst keine ProjectReference auf HaelpMi.UI). Zeigt
/// nach einem erfolgreichen Admin-Installer-Build eine kurze Erfolgsmeldung mit direktem
/// Sprung in den Explorer, statt nur eine Zeile im Protokollfenster.
/// </summary>
public partial class BuildSuccessToastWindow : Window
{
    private string _filePath;
    private readonly DispatcherTimer _autoCloseTimer;

    public BuildSuccessToastWindow(string title, string subText, string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        TitleText.Text = title;
        SubText.Text = subText;

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
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
        Top = targetTop + 36;
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

    private void ShowInExplorerButton_Click(object sender, RoutedEventArgs e)
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

    private void MoveToMountButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();

        try
        {
            _filePath = InstallerMountMover.MoveToMount(_filePath);
            SubText.Text = "Liegt jetzt auf Z:\\HaelpMi-Installer\\";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Verschieben nach Z: fehlgeschlagen:{Environment.NewLine}{ex.Message}",
                "HälpMi Install-Creator", MessageBoxButton.OK, MessageBoxImage.Error);
            _autoCloseTimer.Start();
            return;
        }

        _autoCloseTimer.Interval = TimeSpan.FromSeconds(2);
        _autoCloseTimer.Start();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        AnimateOutAndClose();
    }
}
