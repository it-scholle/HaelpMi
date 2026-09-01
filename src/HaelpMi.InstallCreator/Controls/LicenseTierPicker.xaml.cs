using System.Windows;
using System.Windows.Controls;
// UseWindowsForms+UseWPF sind in diesem Projekt beide aktiv (Clipboard-Retry in
// MainWindow.xaml.cs) - UserControl existiert in beiden, daher hier explizit auflösen.
using UserControl = System.Windows.Controls.UserControl;

namespace HaelpMi.InstallCreator.Controls;

/// <summary>
/// Auswahl-Control für die Lizenz-Paketgröße (XS/Trial, S, M, L, XL), eingehängt im
/// Lizenzen-Reiter des Install-Creators (Issue #18).
/// </summary>
public partial class LicenseTierPicker : UserControl
{
    private static readonly IReadOnlyList<LicenseTierOption> Options =
        Enum.GetValues<LicenseTier>()
            .Select(tier => new LicenseTierOption(tier, LicenseTierLimits.GetDisplayLabel(tier)))
            .ToList();

    public static readonly DependencyProperty SelectedTierProperty = DependencyProperty.Register(
        nameof(SelectedTier), typeof(LicenseTier?), typeof(LicenseTierPicker),
        new PropertyMetadata(null, OnSelectedTierChanged));

    private static readonly DependencyPropertyKey UserLimitPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(UserLimit), typeof(int?), typeof(LicenseTierPicker), new PropertyMetadata(null));

    public static readonly DependencyProperty UserLimitProperty = UserLimitPropertyKey.DependencyProperty;

    private bool _suppressSelectionChanged;

    public LicenseTierPicker()
    {
        InitializeComponent();
        TierComboBox.ItemsSource = Options;
    }

    public LicenseTier? SelectedTier
    {
        get => (LicenseTier?)GetValue(SelectedTierProperty);
        set => SetValue(SelectedTierProperty, value);
    }

    /// <summary>Aus <see cref="SelectedTier"/> abgeleitet, nicht von außen setzbar.</summary>
    public int? UserLimit
    {
        get => (int?)GetValue(UserLimitProperty);
        private set => SetValue(UserLimitPropertyKey, value);
    }

    private static void OnSelectedTierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (LicenseTierPicker)d;
        var tier = (LicenseTier?)e.NewValue;
        picker.UserLimit = tier is null ? null : LicenseTierLimits.GetUserLimit(tier.Value);

        picker._suppressSelectionChanged = true;
        picker.TierComboBox.SelectedItem = tier is null ? null : Options.First(o => o.Tier == tier.Value);
        picker._suppressSelectionChanged = false;
    }

    private void TierComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        SelectedTier = (TierComboBox.SelectedItem as LicenseTierOption)?.Tier;
    }
}
