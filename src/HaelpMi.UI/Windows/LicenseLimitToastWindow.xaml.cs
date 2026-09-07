using System.Windows;
using System.Windows.Media.Animation;

namespace HaelpMi.UI.Windows;

/// <summary>Siehe XAML-Kommentar. "Dashboard öffnen" nur für Admin-Rollen sinnvoll - User-Geräte bekommen nur den Hinweis + Schließen.</summary>
public partial class LicenseLimitToastWindow : Window
{
    public LicenseLimitToastWindow(Action? openDashboard)
    {
        InitializeComponent();

        if (openDashboard is null)
        {
            ButtonsPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            OpenButton.Click += (_, _) =>
            {
                openDashboard();
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
