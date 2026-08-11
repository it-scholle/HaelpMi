using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Ruft ISCC.exe (Inno Setup 6) für HaelpMi-Admin.iss auf. Baut absichtlich AUSSCHLIESSLICH
/// den Admin-Installer (Nutzerwunsch) - der bringt einen Bausatz (Skripte + Nutzlast +
/// einen mitgelieferten Inno-Setup-Compiler) mit hinein, aus dem das Admin-Dashboard beim
/// Kunden später selbst, beliebig oft, einen passenden User-Installer exportiert (siehe
/// HaelpMiCommon.iss.inc [Files]/HaelpMiAdminInstall und AdminDashboardWindow "User-
/// Installer exportieren"). Ein separates "User-Installer erstellen" gibt es hier bewusst
/// nicht mehr - der Sysadmin bekommt vom Anbieter nur die eine Admin-Installer-Datei.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Kein ProjectReference auf HaelpMi.Core (siehe .csproj), daher hier lokal statt
        // LiveIdentityFactory.CurrentProgramVersion.
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        VersionText.Text = $"v{version}";
    }

    private void GeneratePasswordButton_Click(object sender, RoutedEventArgs e) =>
        PasswordBox.Password = GenerateRandomPassword();

    // Keine 0/O/1/l/I - vermeidet Verwechslungen, wenn das Passwort per Telefon/Zettel an
    // den Kunden weitergegeben wird.
    private static string GenerateRandomPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        const int length = 20;

        var builder = new StringBuilder(length);
        foreach (var b in RandomNumberGenerator.GetBytes(length))
        {
            builder.Append(alphabet[b % alphabet.Length]);
        }
        return builder.ToString();
    }

    private void CopyPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            return;
        }

        // Bugfix 06.08.2026 (Fehlerbericht "Kopieren wirft immer noch einen Error"): der
        // vorherige Fix (eigener STA-Worker-Thread + Handmade-Retry-Schleife über
        // System.Windows.Clipboard) war selbst die Fehlerquelle - live nachgestellt und per
        // Get-Clipboard von außen verifiziert: Clipboard.SetText auf einem STA-Thread OHNE
        // eigene Windows-Nachrichtenschleife wirft zuverlässig eine Exception, OBWOHL der
        // Schreibvorgang auf OS-Ebene tatsächlich ankam. Jetzt:
        // System.Windows.Forms.Clipboard.SetDataObject(...) statt der WPF-eigenen
        // Clipboard-Klasse - hat einen offiziell dafür vorgesehenen retryTimes/retryDelay-
        // Parameter für genau CLIPBRD_E_CANT_OPEN und läuft zuverlässig direkt auf dem
        // WPF-UI-Thread (der ist bereits STA, keine eigene Thread-Verwaltung nötig).
        //
        // Dritte Runde desselben Fehlerberichts - Nutzer-Feedback 06.08.2026: "der
        // Kopiervorgang funktioniert ja, es ist wieder in der Zwischenablage - nur die
        // Nachricht nervt und ist falsch". Bestätigt exakt das, was die eigene
        // Live-Nachstellung vorher schon zeigte (siehe Kommentar oben): SetDataObject wirft
        // eine ExternalException aus seinem eigenen internen Render-/Flush-Schritt, OBWOHL
        // der eigentliche Schreibvorgang (SetClipboardData) längst angekommen ist - eine
        // reine Falsch-Meldung, kein echter Fehlschlag. Der vorherige Fix (retryTimes/
        // retryDelay erhöhen) konnte das nicht beheben, weil das Problem nicht "zu wenige
        // Versuche" war, sondern "der letzte Versuch hat geklappt, meldet es aber falsch".
        // Deshalb nicht mehr blind der Exception glauben: bei einem Fehlschlag kurz
        // zurücklesen, bevor überhaupt eine Fehlermeldung gezeigt wird - erst wenn AUCH das
        // fehlschlägt, ist es ein echter Fehler.
        var password = PasswordBox.Password;

        // Bugfix 11.08.2026 (Fehlerbericht "Kopierfehler erscheint wieder" - live per UI
        // Automation nachgestellt, echtes HRESULT 0x800401D0/CLIPBRD_E_CANT_OPEN im
        // Protokoll bestätigt, siehe Chat-Verlauf): kein Regressionsfehler aus der Icon-
        // Umstellung (dieser gesamte Block war seit dem allerersten Fix unverändert), aber
        // der bisherige EINE Anlauf (30 Retries × 100ms ≈ 3s, danach 10×100ms Rücklese-
        // Verify ≈ 1s) reicht nicht, wenn der blockierende Fremdprozess (VM-Zwischenablage-
        // Synchronisation) länger als dieses gesamte Zeitfenster braucht - alle Retries
        // liegen dann im selben Blockierungsfenster. Ein zweiter, komplett frischer Anlauf
        // nach einer kurzen Verschnaufpause (700ms, bewusst außerhalb der engen 100ms-
        // Taktung) trifft mit guter Wahrscheinlichkeit ein anderes Zeitfenster. Blockiert
        // die UI dadurch im schlechtesten Fall knapp 9s statt 4s - für einen manuell
        // angestoßenen Klick in einem internen Entwickler-Werkzeug hinnehmbar.
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(password, copy: true, retryTimes: 30, retryDelay: 100);
                Log("Passwort in die Zwischenablage kopiert.");
                return;
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                if (VerifyClipboardEventuallyMatches(password))
                {
                    // SetDataObject hat sich geirrt (siehe Kommentar oben) - tatsächlich erfolgreich.
                    Log("Passwort in die Zwischenablage kopiert.");
                    return;
                }

                if (attempt < maxAttempts)
                {
                    Log($"Kopieren im {attempt}. Anlauf fehlgeschlagen (HRESULT 0x{ex.ErrorCode:X8}) - neuer Versuch nach kurzer Pause.");
                    System.Threading.Thread.Sleep(700);
                    continue;
                }

                // Erst jetzt, nach zwei vollständigen Anläufen, ein wirklicher Fehlschlag -
                // Nutzer soll wissen, dass das Passwort NICHT sicher kopiert wurde.
                // "Anzeigen"-Knopf ist der tatsächlich funktionierende Fallback - WPFs
                // PasswordBox blockt Strg+C absichtlich (Schutz gegen Mitlesen),
                // "manuell markieren/kopieren" war daher vorher nie umsetzbar.
                var message = $"Kopieren in die Zwischenablage fehlgeschlagen (HRESULT 0x{ex.ErrorCode:X8}) - die Zwischenablage blieb auch nach zwei vollständigen Versuchen (je mehrere Sekunden Wiederholungen) dauerhaft von einem anderen Prozess blockiert (z. B. VM-Zwischenablage-Synchronisation). Über den \"Anzeigen\"-Knopf lässt sich das Passwort anzeigen und stattdessen von Hand markieren/kopieren.";
                Log(message);
                System.Windows.MessageBox.Show(message, "HälpMi Install-Creator", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    // Kurzes Zurücklesen mit ein paar Versuchen - der eigentliche Schreibvorgang ist zu
    // diesem Zeitpunkt entweder schon angekommen (Normalfall, siehe Kommentar oben) oder
    // die Zwischenablage ist wirklich noch blockiert und braucht selbst beim Lesen ein
    // bisschen Geduld.
    private static bool VerifyClipboardEventuallyMatches(string expected)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (System.Windows.Forms.Clipboard.GetText() == expected)
                {
                    return true;
                }
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // weiter versuchen, siehe Schleife
            }

            System.Threading.Thread.Sleep(100);
        }

        return false;
    }

    // Bugfix 06.08.2026 (zweite Runde desselben Fehlerberichts): echter manueller Fallback,
    // falls die automatische Zwischenablage-Kopie (wieder) an Fremd-Blockierung scheitert -
    // WPFs PasswordBox verweigert Strg+C grundsätzlich, "einfach markieren und kopieren" war
    // also nie eine tatsächlich befolgbare Anweisung. PasswordBox und PasswordRevealBox
    // liegen in der XAML übereinander in derselben Grid-Zelle, nur eine ist je sichtbar.
    private bool _isSyncingPasswordControls;
    private bool _passwordRevealed;

    private void RevealPasswordToggle_Click(object sender, RoutedEventArgs e)
    {
        _passwordRevealed = !_passwordRevealed;
        if (_passwordRevealed)
        {
            PasswordRevealBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordRevealBox.Visibility = Visibility.Visible;
            EyeOpenIcon.Visibility = Visibility.Collapsed;
            EyeClosedIcon.Visibility = Visibility.Visible;
            RevealPasswordToggle.ToolTip = "Passwort wieder verbergen";
            PasswordRevealBox.Focus();
            PasswordRevealBox.SelectAll();
        }
        else
        {
            PasswordBox.Password = PasswordRevealBox.Text;
            PasswordRevealBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            EyeClosedIcon.Visibility = Visibility.Collapsed;
            EyeOpenIcon.Visibility = Visibility.Visible;
            RevealPasswordToggle.ToolTip = "Passwort anzeigen (zum manuellen Markieren/Kopieren, falls die Zwischenablage nicht mitmacht)";
        }
    }

    // Beide Felder bleiben synchron, unabhängig davon, welches gerade sichtbar ist - alle
    // anderen Stellen (TryValidate, BuildAdminButton_Click, CopyPasswordButton_Click) lesen
    // weiterhin ausschließlich PasswordBox.Password, das muss also immer aktuell sein, auch
    // wenn der Nutzer im "anzeigen"-Modus tippt. _isSyncingPasswordControls verhindert eine
    // Endlosschleife zwischen den beiden Changed-Handlern.
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingPasswordControls)
        {
            return;
        }

        _isSyncingPasswordControls = true;
        try
        {
            PasswordRevealBox.Text = PasswordBox.Password;
        }
        finally
        {
            _isSyncingPasswordControls = false;
        }
    }

    private void PasswordRevealBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingPasswordControls)
        {
            return;
        }

        _isSyncingPasswordControls = true;
        try
        {
            PasswordBox.Password = PasswordRevealBox.Text;
        }
        finally
        {
            _isSyncingPasswordControls = false;
        }
    }

    private async void BuildAdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidate(out var error))
        {
            System.Windows.MessageBox.Show(error, "HälpMi Install-Creator", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var customerGroupId = Guid.NewGuid(); // FR-49: fest für dieses Admin-Installer-Paket und jeden späteren daraus exportierten User-Installer
        var password = PasswordBox.Password;
        var isTestInstaller = TestInstallerCheckBox.IsChecked == true;
        var customerNameOrTestLabel = CustomerNameBox.Text.Trim();

        SetBusy(true);
        try
        {
            await BuildAdminInstallerAsync(customerGroupId, isTestInstaller, password, customerNameOrTestLabel);
        }
        catch (Exception ex)
        {
            // Bisher landete eine Ausnahme hier nur im (seit heute vorhandenen) globalen
            // Crash-Log, ohne dass im Protokollfenster selbst irgendetwas zu sehen war -
            // derselbe "Button tut scheinbar nichts"-Effekt wie beim Admin-Dashboard.
            Log($"Unerwarteter Fehler: {ex.Message}");
            CrashLogger.Log("BuildAdminButton_Click", ex);
            System.Windows.MessageBox.Show($"Admin-Installer-Erstellung fehlgeschlagen:{Environment.NewLine}{ex.Message}",
                "HälpMi Install-Creator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool TryValidate(out string error)
    {
        var isTest = TestInstallerCheckBox.IsChecked == true;
        if (!isTest)
        {
            if (string.IsNullOrWhiteSpace(CustomerNameBox.Text))
            {
                error = "Kundenname ist bei einem Produktiv-Installer Pflicht.";
                return false;
            }

            if (string.IsNullOrEmpty(PasswordBox.Password))
            {
                error = "Installer-Passwort ist bei einem Produktiv-Installer Pflicht.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private void SetBusy(bool busy)
    {
        BuildAdminButton.IsEnabled = !busy;
        TestInstallerCheckBox.IsEnabled = !busy;
        CustomerNameBox.IsEnabled = !busy;
    }

    private async Task BuildAdminInstallerAsync(Guid customerGroupId, bool isTestInstaller, string password, string customerNameOrTestLabel)
    {
        Log("--- Installer werden erstellt ---");
        Log($"Kunden-Gruppen-ID: {customerGroupId}");
        Log($"Test-Installer: {(isTestInstaller ? "ja" : "nein")}");
        if (isTestInstaller && customerNameOrTestLabel.Length > 0)
        {
            Log($"Test-Bezeichnung: {customerNameOrTestLabel}");
        }
        // Das Passwort selbst landet absichtlich nie im Protokoll - "keine zentrale
        // Passwort-Speicherung" gilt auch fürs Protokollfenster, nicht nur für Dateien.

        var installerDir = FindInstallerDirectory();

        var isccPath = FindIscc();
        if (isccPath is null)
        {
            Log("Fehler: ISCC.exe (Inno Setup 6 Compiler) wurde nicht gefunden. " +
                "Bitte Inno Setup installieren oder ISCC.exe zum PATH hinzufügen.");
            return;
        }

        var userScriptPath = Path.Combine(installerDir, "HaelpMi.iss");
        var adminScriptPath = Path.Combine(installerDir, "HaelpMi-Admin.iss");
        if (!File.Exists(userScriptPath) || !File.Exists(adminScriptPath))
        {
            Log($"Fehler: {userScriptPath} oder {adminScriptPath} wurde nicht gefunden.");
            return;
        }

        // Bugfix 11.08.2026 (Fehlerbericht "Fix war im Installer nicht drin, obwohl main
        // längst korrekt war"): installer/payload/ ist ein reiner Datei-Cache, den bisher
        // niemand automatisch aktuell hielt - Install-Creator hat ISCC bis hierhin klaglos
        // auf einen tagealten Payload losgelassen, ohne das zu bemerken (ISCC kompiliert
        // keinen C#-Code, es kopiert nur, was im Ordner liegt). Jetzt läuft der Publish
        // IMMER zuerst mit, garantiert aktuellen Code statt sich auf "vorher dran gedacht"
        // zu verlassen.
        if (!await RefreshPayloadAsync(installerDir))
        {
            Log("Payload-Aktualisierung fehlgeschlagen - Installer wird nicht gebaut.");
            return;
        }

        // Nutzer-Wunsch 04.08.2026: der User-Installer wird nicht mehr auf dem
        // Kundenrechner live nachgebaut (siehe HaelpMiCommon.iss.inc-Kommentar), sondern
        // hier EINMALIG fertig kompiliert und danach als bereits fertige Datei in den
        // Admin-Installer eingebettet - Reihenfolge ist deshalb zwingend User vor Admin.
        var userPayloadDir = Path.Combine(installerDir, "UserInstallerPayload");
        Directory.CreateDirectory(userPayloadDir);
        Log("Schritt 2/3: User-Installer wird kompiliert (für die Einbettung in den Admin-Installer)...");
        // Nutzerfrage 06.08.2026 ("braucht der User-Installer wirklich ein Passwort?"): nein -
        // das Passwort gilt bewusst nur für den Admin-Installer (siehe XAML-Kommentar beim
        // PasswordBox). Bewusst KEIN /DInstallerPassword hier, auch wenn eines gesetzt ist.
        var userExitCode = await RunIsccAsync(isccPath, installerDir, userScriptPath, args =>
        {
            args.Add($"/DCustomerGroupId={customerGroupId}");
            args.Add($"/DIsTestInstaller={(isTestInstaller ? "true" : "false")}");
            args.Add($"/O{userPayloadDir}");
            args.Add("/FHaelpMi-User-Setup");
        });
        if (userExitCode != 0)
        {
            Log($"User-Installer-Kompilierung fehlgeschlagen (ISCC-Exitcode {userExitCode}) - Admin-Installer wird nicht gebaut.");
            return;
        }
        if (!File.Exists(Path.Combine(userPayloadDir, "HaelpMi-User-Setup.exe")))
        {
            Log($"Fehler: User-Installer-Kompilierung meldete Erfolg, aber {Path.Combine(userPayloadDir, "HaelpMi-User-Setup.exe")} fehlt.");
            return;
        }

        Log("Schritt 3/3: Admin-Installer wird kompiliert (bettet den eben gebauten User-Installer ein)...");
        var adminExitCode = await RunIsccAsync(isccPath, installerDir, adminScriptPath, args =>
        {
            args.Add($"/DCustomerGroupId={customerGroupId}");
            args.Add($"/DIsTestInstaller={(isTestInstaller ? "true" : "false")}");
            if (!string.IsNullOrEmpty(password))
            {
                args.Add($"/DInstallerPassword={password}");
            }
            // Bei mehreren Test-Installern nacheinander sonst immer derselbe Dateiname
            // ("...-Admin-<Version>.exe") - mit Test-Bezeichnung bleiben sie im Output-
            // Ordner unterscheidbar, ohne dass man sie manuell umbenennen müsste.
            if (isTestInstaller && customerNameOrTestLabel.Length > 0)
            {
                args.Add($"/FHaelpMi-Setup-Admin-Test-{SanitizeForFileName(customerNameOrTestLabel)}");
            }
        });

        if (adminExitCode == 0)
        {
            var outputDir = Path.Combine(installerDir, "Output");
            Log($"Admin-Installer erfolgreich erstellt (siehe {outputDir}). " +
                "Das ist die einzige Datei, die an den Sysadmin geht.");
            ShowSuccessToast(outputDir);
        }
        else
        {
            Log($"Admin-Installer-Erstellung fehlgeschlagen (ISCC-Exitcode {adminExitCode}).");
        }
    }

    // installer/HaelpMi.iss (Kopfkommentar) dokumentiert dieselben drei Befehle als
    // manuellen Schritt für alle, die ohne Install-Creator direkt per ISCC bauen (z. B.
    // schnelles lokales Testen, siehe BUILD-UND-INSTALLATION.md) - hier laufen sie
    // automatisch vor jedem Install-Creator-Build mit.
    private static readonly string[] PayloadProjects =
    {
        Path.Combine("src", "HaelpMi.Agent", "HaelpMi.Agent.csproj"),
        Path.Combine("src", "HaelpMi.Config", "HaelpMi.Config.csproj"),
        Path.Combine("src", "HaelpMi.UpdateService", "HaelpMi.UpdateService.csproj"),
    };

    private async Task<bool> RefreshPayloadAsync(string installerDir)
    {
        var repoRoot = Directory.GetParent(installerDir)?.FullName;
        if (repoRoot is null)
        {
            Log("Fehler: Repo-Wurzel (oberhalb von installer/) konnte nicht bestimmt werden.");
            return false;
        }

        var payloadDir = Path.Combine(installerDir, "payload");
        Directory.CreateDirectory(payloadDir);

        for (var i = 0; i < PayloadProjects.Length; i++)
        {
            var project = Path.Combine(repoRoot, PayloadProjects[i]);
            if (!File.Exists(project))
            {
                Log($"Fehler: {project} nicht gefunden.");
                return false;
            }

            Log($"Schritt 1/3: Payload wird aktualisiert ({i + 1}/{PayloadProjects.Length}: {Path.GetFileNameWithoutExtension(project)})...");
            var exitCode = await RunProcessAsync("dotnet", repoRoot, args =>
            {
                args.Add("publish");
                args.Add(project);
                args.Add("-c");
                args.Add("Release");
                args.Add("-r");
                args.Add("win-x64");
                args.Add("-p:Platform=x64");
                args.Add("--self-contained");
                args.Add("true");
                args.Add("-p:PublishReadyToRun=true");
                args.Add("-o");
                args.Add(payloadDir);
            }, "[dotnet publish] ");

            if (exitCode != 0)
            {
                Log($"Fehler: dotnet publish für {Path.GetFileName(project)} fehlgeschlagen (Exitcode {exitCode}).");
                return false;
            }
        }

        return true;
    }

    private Task<int> RunIsccAsync(string isccPath, string workingDirectory, string scriptPath, Action<System.Collections.Generic.IList<string>> addArgs) =>
        RunProcessAsync(isccPath, workingDirectory, args =>
        {
            // ArgumentList statt eines zusammengesetzten Kommandozeilen-Strings - vermeidet
            // Escaping-Fehler/Injection, falls Kundenname oder Passwort Sonderzeichen enthalten.
            addArgs(args);
            args.Add(scriptPath);
        }, "[ISCC] ");

    private async Task<int> RunProcessAsync(string exePath, string workingDirectory, Action<System.Collections.Generic.IList<string>> addArgs, string errorLogPrefix)
    {
        var startInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        addArgs(startInfo.ArgumentList);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                Dispatcher.Invoke(() => Log(args.Data));
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                Dispatcher.Invoke(() => Log(errorLogPrefix + args.Data));
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    // Sucht statt eines fest angenommenen Dateinamens die zuletzt geschriebene .exe im
    // Output-Ordner - robust gegenüber der je nach Test-Bezeichnung wechselnden
    // Ausgabedatei (siehe /F-Override oben), ohne die ISCC-Versionsnummer selbst kennen
    // zu müssen (die steckt nur in der .iss, nicht hier im C#-Code).
    private string? _lastBuiltInstallerPath;

    private void ShowSuccessToast(string outputDir)
    {
        try
        {
            var newestExe = new DirectoryInfo(outputDir)
                .GetFiles("*.exe")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newestExe is null)
            {
                return; // Log-Zeile oben reicht dann als einzige Rückmeldung
            }

            // Nutzerwunsch 06.08.2026: der Toast schließt nach 8s von selbst - dieses Panel
            // bleibt zusätzlich im Hauptfenster stehen, falls der Toast übersehen wurde
            // (siehe MainWindow.xaml, LastBuildPanel).
            _lastBuiltInstallerPath = newestExe.FullName;
            LastBuildFileText.Text = newestExe.Name + " liegt bereit für den Sysadmin.";
            LastBuildPanel.Visibility = Visibility.Visible;

            var toast = new BuildSuccessToastWindow(
                "Admin-Installer erstellt",
                newestExe.Name + " liegt bereit für den Sysadmin.",
                newestExe.FullName);
            toast.Show();
        }
        catch (Exception)
        {
            // best-effort - das Protokoll oben hat die Erfolgsmeldung bereits geloggt
        }
    }

    private void LastBuildShowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastBuiltInstallerPath is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastBuiltInstallerPath}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // best-effort - gleiches Muster wie BuildSuccessToastWindow.ShowInExplorerButton_Click
        }
    }

    // Sucht sowohl Inno Setup 7 (aktuell) als auch 6 (falls das schon vorhanden ist -
    // laut Hersteller weitgehend abwärtskompatibel, unsere Skripte brauchen keine der
    // beiden Versionen zwingend). Deckt sowohl die "für alle Nutzer" (Program Files) als
    // auch die "nur für mich" Installationsoption ab (%LOCALAPPDATA%\Programs\...).
    private static string? FindIscc()
    {
        var versionFolderNames = new[] { "Inno Setup 7", "Inno Setup 6" };
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };

        foreach (var root in roots)
        {
            foreach (var versionFolderName in versionFolderNames)
            {
                var candidate = Path.Combine(root, versionFolderName, "ISCC.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, "ISCC.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // Install-Creator läuft sowohl per "dotnet run" aus dem Repo als auch als eigenständig
    // gebautes exe irgendwo daneben - deshalb ausgehend vom Ausführungsverzeichnis nach
    // oben zum "installer"-Ordner suchen, statt einen festen relativen Pfad anzunehmen.
    private static string FindInstallerDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "installer");
            if (File.Exists(Path.Combine(candidate, "HaelpMi.iss")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "installer");
    }

    private static string SanitizeForFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(invalid.Contains(c) || c == ' ' ? '-' : c);
        }
        return builder.ToString();
    }

    private void Log(string message)
    {
        LogBox.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        LogBox.ScrollToEnd(); // Nutzerwunsch: Protokoll immer "live" am Ende, kein manuelles Nachscrollen
    }
}
