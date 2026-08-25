using System.Diagnostics;
using System.Windows;
using HaelpMi.Core.Licensing;

namespace HaelpMi.UI.Windows;

/// <summary>
/// Bestätigungsdialog vor "Neue Lizenz anfordern" (Issue #20) - der Admin sieht den
/// fertigen Mailtext vorab und öffnet das Standard-Mailprogramm erst per bewusstem Klick
/// (<see cref="LicenseRenewalMailBuilder"/> liefert Empfänger/Betreff/Text), statt dass ein
/// Klick auf den Auslöser-Button direkt ungefragt eine Mail aufreißt.
/// </summary>
public partial class LicenseRenewalMailConfirmWindow : Window
{
    private readonly LicenseRenewalMailContent _mail;

    public LicenseRenewalMailConfirmWindow(LicenseRenewalMailContent mail)
    {
        InitializeComponent();
        _mail = mail;

        ToText.Text = mail.To;
        SubjectText.Text = mail.Subject;
        BodyText.Text = mail.Body;
    }

    private void OpenMailButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_mail.ToMailtoUri().ToString()) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // best-effort - kein Absturz, falls kein Standard-Mailprogramm registriert ist
        }

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
