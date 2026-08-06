using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HaelpMi.Installer.Tests;

/// <summary>
/// Steuert Inno Setups eigene Wizard-Seiten und native MsgBox-Rückfragen per Tastatur -
/// bewusst NICHT per Koordinaten-Klick oder WPF-spezifischer UI-Automation, weil das hier
/// gar nicht unsere eigene, sich verändernde Oberfläche ist, sondern Inno Setups feste,
/// über Jahre stabile Wizard-Chrome (siehe TEST-STRATEGY.md, Abschnitt "Warum nicht alles
/// jetzt automatisieren" - genau der Unterschied zwischen stabiler und sich verändernder
/// UI). Tab-Reihenfolge und Beschleuniger (Alt+N für "&Weiter" usw.) sind Teil von Inno
/// Setups eigenem, unverändertem VCL-Wizard und nicht von uns beeinflusst.
///
/// EINZIGE Stelle, an der die Reihenfolge der Wizard-Seiten hart kodiert ist - ändert sich
/// [Setup]/[Code] der .iss-Skripte (z. B. eine neue Wizard-Seite), muss NUR diese Klasse
/// angepasst werden, nicht jeder einzelne Test.
/// </summary>
internal static class WizardAutomation
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Vollständige Ersteinrichtung: Willkommen → Zielverzeichnis → Raum-Zuordnung (FR-37,
    /// einzige Seite mit echter Eingabe) → Bereit → Installieren → Fertig. Kein
    /// [Tasks]-Abschnitt und DisableProgramGroupPage=yes (siehe HaelpMi.iss) - daher genau
    /// diese vier Klick-Seiten plus die Fertig-Seite, keine weiteren.
    /// </summary>
    public static async Task RunFirstInstallWizardAsync(Process installerProcess, string roomName, string roomNumber, string expectedResultFile)
    {
        await WaitForWindowAsync(installerProcess);

        FocusAndSend(installerProcess, "%n"); // Willkommen: Alt+N ("&Weiter")
        await Task.Delay(TimeSpan.FromSeconds(1));

        FocusAndSend(installerProcess, "%n"); // Zielverzeichnis: Alt+N (Standardpfad übernehmen)
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Raum-Zuordnung (RoomPage, siehe HaelpMiCommon.iss.inc InitializeWizard): erstes
        // Eingabefeld hat beim Seitenwechsel bereits den Fokus, kein Tab nötig davor.
        FocusAndSend(installerProcess, roomName);
        FocusAndSend(installerProcess, "{TAB}");
        FocusAndSend(installerProcess, roomNumber);
        FocusAndSend(installerProcess, "%n");
        await Task.Delay(TimeSpan.FromSeconds(1));

        FocusAndSend(installerProcess, "%i"); // Bereit-Seite: Alt+I ("&Installieren")

        // Datei-Kopieren braucht echte Zeit (Payload ist mehrere hundert MB, siehe
        // BUILD-UND-INSTALLATION.md) - auf das tatsächliche Ergebnis pollen statt eine feste
        // Wartezeit zu raten, die bei einer langsamen Test-VM sonst zu kurz sein könnte.
        await WaitForFileAsync(expectedResultFile, TimeSpan.FromMinutes(2));
        await Task.Delay(TimeSpan.FromMilliseconds(500)); // CurStepChanged(ssPostInstall) ganz zu Ende laufen lassen

        FocusAndSend(installerProcess, "%f"); // Fertig-Seite: Alt+F ("&Fertigstellen") - startet ggf. postinstall-Run-Einträge, siehe Aufrufer-Cleanup
    }

    /// <summary>Update/Reparatur/Downgrade-Versuch: settings.json existiert schon, RoomPage wird übersprungen (ShouldSkipPage) - nur Willkommen → Zielverzeichnis → Bereit → Fertig.</summary>
    public static async Task RunUpdateWizardAsync(Process installerProcess, string expectedResultFile)
    {
        await WaitForWindowAsync(installerProcess);

        FocusAndSend(installerProcess, "%n"); // Willkommen
        await Task.Delay(TimeSpan.FromSeconds(1));
        FocusAndSend(installerProcess, "%n"); // Zielverzeichnis
        await Task.Delay(TimeSpan.FromSeconds(1));
        FocusAndSend(installerProcess, "%i"); // Bereit → Installieren

        await WaitForFileAsync(expectedResultFile, TimeSpan.FromMinutes(2));
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        FocusAndSend(installerProcess, "%f"); // Fertig
    }

    /// <summary>
    /// Beantwortet InitializeUninstall()s native MsgBox ("Sollen auch die lokal
    /// gespeicherten Einstellungen... entfernt werden?", MB_YESNO) per Tastatur-
    /// Beschleuniger - Windows bindet Y/N bei einer Standard-MessageBox immer direkt,
    /// kein Tab/Enter nötig.
    /// </summary>
    public static async Task RunUninstallWithDataPromptAsync(Process uninstallerProcess, bool keepData)
    {
        await WaitForWindowAsync(uninstallerProcess);
        await Task.Delay(TimeSpan.FromMilliseconds(500)); // MsgBox erscheint minimal nach dem Hauptfenster

        FocusAndSend(uninstallerProcess, keepData ? "n" : "y");
    }

    private static void FocusAndSend(Process process, string keys)
    {
        process.Refresh();
        if (process.MainWindowHandle != IntPtr.Zero)
        {
            SetForegroundWindow(process.MainWindowHandle);
        }

        SendKeys.SendWait(keys);
    }

    private static async Task WaitForWindowAsync(Process process, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Installer-/Deinstaller-Fenster ist nicht rechtzeitig erschienen.");
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"{path} ist nach {timeout} nicht erschienen - Installation vermutlich hängengeblieben oder fehlgeschlagen.");
    }
}
