using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;

namespace HaelpMi.UI.Windows;

/// <summary>Siehe XAML-Kommentar. <paramref name="findUninstallerExecutablePath"/> kommt aus AppPaths.FindUninstallerExecutable (HaelpMi.Core), damit dieses UI-Projekt keine Storage-Abhängigkeit braucht - gleiches Delegationsmuster wie AdminDashboardContext.</summary>
public partial class DeviceRemovedToastWindow : Window
{
    public DeviceRemovedToastWindow(Func<string?> findUninstallerExecutablePath)
    {
        InitializeComponent();

        UninstallButton.Click += (_, _) =>
        {
            var uninstallerPath = findUninstallerExecutablePath();
            if (uninstallerPath is null)
            {
                UninstallerMissingText.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(uninstallerPath) { UseShellExecute = true });
                AnimateOutAndClose();
            }
            catch (Exception)
            {
                // Inno-Setup-Uninstaller kann z. B. fehlende Rechte melden - lieber den
                // manuellen Hinweis zeigen als hier kommentarlos nichts zu tun.
                UninstallerMissingText.Visibility = Visibility.Visible;
            }
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
    }

    private void AnimateOutAndClose()
    {
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => AnimateOutAndClose();
}
