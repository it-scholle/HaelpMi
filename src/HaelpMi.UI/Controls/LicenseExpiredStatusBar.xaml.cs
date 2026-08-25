using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HaelpMi.Core.Licensing;
using HaelpMi.UI.Windows;

namespace HaelpMi.UI.Controls;

/// <summary>
/// Stiller Dauerhinweis für Stufe 4 (Issue #20) - bewusst getrennt vom Toast: keine eigene
/// Unterbrechung, bleibt einfach in der Statusleiste stehen, solange die Lizenz abgelaufen
/// ist (kein Hard-Lock, aber "Hinweis bleibt sichtbar" laut Testplan). Noch nirgends
/// eingebunden - der Aufrufer entscheidet, wann er diese Control anzeigt, z. B. nur wenn
/// <see cref="LicenseWarningEvaluator.GetStage"/> <see cref="LicenseWarningStage.Expired"/> liefert.
/// </summary>
public partial class LicenseExpiredStatusBar : UserControl
{
    public static readonly DependencyProperty ExpiresAtUtcProperty = DependencyProperty.Register(
        nameof(ExpiresAtUtc), typeof(DateTime), typeof(LicenseExpiredStatusBar),
        new PropertyMetadata(DateTime.UtcNow, OnDisplayRelevantPropertyChanged));

    public static readonly DependencyProperty CustomerGroupIdProperty = DependencyProperty.Register(
        nameof(CustomerGroupId), typeof(Guid), typeof(LicenseExpiredStatusBar), new PropertyMetadata(Guid.Empty));

    public DateTime ExpiresAtUtc
    {
        get => (DateTime)GetValue(ExpiresAtUtcProperty);
        set => SetValue(ExpiresAtUtcProperty, value);
    }

    public Guid CustomerGroupId
    {
        get => (Guid)GetValue(CustomerGroupIdProperty);
        set => SetValue(CustomerGroupIdProperty, value);
    }

    public LicenseExpiredStatusBar()
    {
        InitializeComponent();
        RefreshText();
    }

    private static void OnDisplayRelevantPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LicenseExpiredStatusBar)d).RefreshText();

    private void RefreshText()
    {
        var subtext = LicenseWarningTextFormatter.SubtextFor(LicenseWarningStage.Expired, ExpiresAtUtc, DateTime.UtcNow);
        MessageText.Text = $"HälpMi-Lizenz abgelaufen {subtext} - die Nutzung ist weiterhin möglich.";
    }

    private void ActionLink_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var mail = LicenseRenewalMailBuilder.Build(CustomerGroupId, ExpiresAtUtc);
        new LicenseRenewalMailConfirmWindow(mail) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
