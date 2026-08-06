using System.Windows;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Entwickler-only Tool (Task "Install-Creator", FR-35/FR-49): erzeugt pro Kunde ein
/// zusammengehöriges Admin-/User-Installer-Paar mit gemeinsamer Kunden-/Gruppen-ID, indem
/// es ISCC.exe (Inno Setup Compiler, Version 6 oder 7) mit entsprechenden /D-Parametern aufruft. Läuft nie
/// beim Kunden, ist kein Teil der ausgelieferten Payload.
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CrashLogger.InstallProcessWideHooks();
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLogger.Log("DispatcherUnhandledException", args.Exception);
            // Ein Entwickler-Werkzeug, das mitten in einer Kunden-Auslieferung wegstirbt,
            // ist schlimmer als eines, das nach einem geloggten Fehler einfach weiterläuft -
            // eingetragener Kundenname/Passwort im Fenster bleiben so erhalten.
            args.Handled = true;
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
