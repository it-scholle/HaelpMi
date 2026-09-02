using System.Windows;

namespace HaelpMi.UI.Windows;

/// <summary>
/// Kleiner Modal-Dialog für Issue #51/#54-Nacharbeit: nimmt den vom Nutzer eingefügten
/// Lizenzschlüssel-Text entgegen und ruft <see cref="ViewModels.AdminDashboardContext.ImportLicenseKeyText"/>
/// auf - kein Datei-Dialog, siehe XAML-Kommentar.
/// </summary>
public partial class LicenseKeyImportWindow : Window
{
    private readonly Func<string, Core.Licensing.LicenseImportResult> _importLicenseKeyText;

    public LicenseKeyImportWindow(Func<string, Core.Licensing.LicenseImportResult> importLicenseKeyText)
    {
        InitializeComponent();
        _importLicenseKeyText = importLicenseKeyText;
        Loaded += (_, _) => KeyTextBox.Focus();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var keyText = KeyTextBox.Text.Trim();
        if (keyText.Length == 0)
        {
            ShowError("Bitte einen Lizenzschlüssel einfügen.");
            return;
        }

        var result = _importLicenseKeyText(keyText);
        if (!result.Success)
        {
            var reason = result.CheckResult.Status == Core.Licensing.LicenseStatus.Missing
                ? "Der Text konnte nicht gelesen werden."
                : "Der Schlüssel ist keine gültige Lizenz für diese Installation (Signatur oder Kundengruppe passt nicht).";
            ShowError(reason);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
