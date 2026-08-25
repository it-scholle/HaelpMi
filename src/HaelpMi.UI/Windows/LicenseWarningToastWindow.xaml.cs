using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;
using HaelpMi.Core.Licensing;

namespace HaelpMi.UI.Windows;

public enum LicenseWarningDismissAction
{
    None,
    Ignored,
    Snoozed,
}

/// <summary>
/// Nicht-blockierender Ecken-Hinweis vor Lizenzablauf (Issue #20) - gleiche Machart wie
/// <see cref="DownloadToastWindow"/> (randlos, schwebt unten rechts ein, Topmost ohne das
/// Hauptfenster zu sperren), aber bewusst ohne Auto-Close-Timer (Nutzerfeedback: "soll
/// sich nicht automatisch ausblenden") und ohne Ton. Baut sich komplett aus dem
/// übergebenen Ablaufdatum auf - noch nicht an einen echten Login-/Boot-Flow angebunden,
/// das ist bewusst Sache des aufrufenden Codes.
/// </summary>
public partial class LicenseWarningToastWindow : Window
{
    private readonly LicenseWarningStage _stage;
    private readonly Guid _customerGroupId;
    private readonly DateTime _expiresAtUtc;

    public LicenseWarningDismissAction ResultAction { get; private set; } = LicenseWarningDismissAction.None;

    public TimeSpan? SelectedSnooze { get; private set; }

    public LicenseWarningToastWindow(LicenseWarningStage stage, DateTime expiresAtUtc, Guid customerGroupId)
    {
        InitializeComponent();
        _stage = stage;
        _customerGroupId = customerGroupId;
        _expiresAtUtc = expiresAtUtc;

        TitleText.Text = LicenseWarningTextFormatter.TitleFor(stage);
        SubText.Text = LicenseWarningTextFormatter.SubtextFor(stage, expiresAtUtc, DateTime.UtcNow);

        ApplyStageAppearance(stage);
        BuildSnoozeOptions(stage);

        Loaded += (_, _) => AnimateIn();
    }

    private void ApplyStageAppearance(LicenseWarningStage stage)
    {
        if (stage == LicenseWarningStage.Expired)
        {
            CardBorder.Background = Brush("#FF3B30");
            CardBorder.BorderThickness = new Thickness(0);
            DotIndicator.Fill = Brushes.White;
            TitleText.Foreground = Brushes.White;
            SubText.Foreground = Brush("#FFD9D5");
            CloseButton.Foreground = Brushes.White;
            ActionButton.Background = Brushes.White;
            ActionButton.Foreground = Brush("#FF3B30");
            return;
        }

        // Eine Farbfamilie, blass -> etwas wärmer - kein Hue-Sprung zwischen den Stufen
        // (Nutzerfeedback), volles Rot bleibt allein Stufe 4 vorbehalten.
        var (background, border, dot) = stage switch
        {
            LicenseWarningStage.Reminder => (Brush("#FBE3DD"), Brush("#F1D3CA"), Brush("#BD6A5C")),
            LicenseWarningStage.Urgent => (Brush("#F8D2C6"), Brush("#EEC0B0"), Brush("#A83D2C")),
            _ => (Brush("#FFFFFF"), Brush("#ECDEDB"), Brush("#97726B")), // EarlyNotice
        };

        CardBorder.Background = background;
        CardBorder.BorderBrush = border;
        DotIndicator.Fill = dot;
        TitleText.Foreground = Brush("#1D1D1F");
        SubText.Foreground = Brush("#6E6E73");
        CloseButton.Foreground = Brush("#6E6E73");
        ActionButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
    }

    private void BuildSnoozeOptions(LicenseWarningStage stage)
    {
        var options = LicenseWarningSnoozeOptions.For(stage);
        if (options.Count == 0)
        {
            SnoozeButton.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var option in options)
        {
            var optionButton = new Button
            {
                Content = option.Label,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            optionButton.Click += (_, _) => OnSnoozeOptionPicked(option.Duration);
            SnoozeOptionsPanel.Children.Add(optionButton);
        }
    }

    private void OnSnoozeOptionPicked(TimeSpan duration)
    {
        SelectedSnooze = duration;
        ResultAction = LicenseWarningDismissAction.Snoozed;
        SnoozePopup.IsOpen = false;
        AnimateOutAndClose();
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e) => SnoozePopup.IsOpen = true;

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        var mail = LicenseRenewalMailBuilder.Build(_customerGroupId, _expiresAtUtc);
        new LicenseRenewalMailConfirmWindow(mail) { Owner = this }.ShowDialog();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Stufe 4 kennt kein "Ignorieren" - sie kommt ohnehin bei jedem Login wieder.
        ResultAction = _stage == LicenseWarningStage.Expired
            ? LicenseWarningDismissAction.None
            : LicenseWarningDismissAction.Ignored;
        AnimateOutAndClose();
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

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
