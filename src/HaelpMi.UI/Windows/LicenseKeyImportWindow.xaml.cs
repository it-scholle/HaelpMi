using System.Windows;
using HaelpMi.Core.Licensing;

namespace HaelpMi.UI.Windows;

/// <summary>
/// Kleiner Modal-Dialog für Issue #51/#54-Nacharbeit: nimmt den vom Nutzer eingefügten
/// Lizenzschlüssel-Text entgegen und ruft <see cref="ViewModels.AdminDashboardContext.ImportLicenseKeyText"/>
/// auf - kein Datei-Dialog, siehe XAML-Kommentar.
///
/// Nutzervorgabe (02.09.2026): jede Rückmeldung unterscheidet konkret, was passiert ist -
/// Aktivierung mit Paketgröße+Zeitraum, oder eine der Ablehnungsgründe aus
/// <see cref="LicenseImportOutcome"/>, statt einer einzigen generischen Meldung.
/// </summary>
public partial class LicenseKeyImportWindow : Window
{
    private readonly Func<string, LicenseImportDiagnosis> _importLicenseKeyText;

    public LicenseKeyImportWindow(Func<string, LicenseImportDiagnosis> importLicenseKeyText)
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

        var diagnosis = _importLicenseKeyText(keyText);
        if (diagnosis.Outcome != LicenseImportOutcome.Activated)
        {
            ShowError(BuildErrorMessage(diagnosis));
            return;
        }

        var license = diagnosis.License!;
        var packageLabel = license.UserLimit is { } limit ? $"{license.Tier} - {limit} Nutzer" : $"{license.Tier} - unbegrenzt";
        MessageBox.Show(this,
            $"Lizenzschlüssel aktiviert.{Environment.NewLine}Paketgröße: {packageLabel}{Environment.NewLine}Gültig bis: {license.ExpiryDateUtc.ToLocalTime():d}",
            "HälpMi - Lizenzschlüssel aktiviert", MessageBoxButton.OK, MessageBoxImage.Information);

        DialogResult = true;
        Close();
    }

    private static string BuildErrorMessage(LicenseImportDiagnosis diagnosis) => diagnosis.Outcome switch
    {
        LicenseImportOutcome.NotRecognized => "Der eingefügte Text wurde nicht als Lizenzschlüssel erkannt (falsch kopiert, unvollständig, oder Signatur ungültig).",
        LicenseImportOutcome.WrongCustomer => "Dieser Lizenzschlüssel ist für eine andere Installation ausgestellt und passt nicht zu dieser Kundengruppe.",
        LicenseImportOutcome.Expired => $"Dieser Lizenzschlüssel ist bereits seit {diagnosis.License!.ExpiryDateUtc:d} abgelaufen. Bitte einen aktuellen Schlüssel anfordern.",
        _ => "Unbekannter Fehler beim Einspielen.",
    };

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
