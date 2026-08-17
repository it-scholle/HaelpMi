using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using HaelpMi.UpdateSigner;

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
    // Update-Schlüssel-Zustand (Nutzerwunsch 13.08.2026, "Update-Ei"): ausschließlich im
    // Arbeitsspeicher dieses Programmlaufs, nie auf der Platte - siehe VaultwardenClient.
    private VaultwardenClient? _vaultwardenClient;
    private string? _vaultSessionKey;
    private byte[]? _updatePrivateKeyBytes;

    // Separater Test-Key (Nutzerwunsch 16.08.2026): eigene Vaultwarden-Notiz
    // (VaultwardenClient.UpdateTestPrivateKeyItemName), sonst 1:1 identisch gehandhabt wie
    // _updatePrivateKeyBytes - nur eben ausschließlich für Test-Installer-Builds genutzt.
    private byte[]? _updateTestPrivateKeyBytes;

    // Admin-Installer/Update-Paket/Update erstellen dürfen nie gleichzeitig laufen (siehe
    // SetBusy) UND brauchen alle drei zwingend einen geladenen Schlüssel (Nutzerwunsch
    // 16.08.2026) - ein Feld statt eines Methodenparameters, weil UpdateBuildButtonsEnabledState
    // auch außerhalb von SetBusy aufgerufen wird (Schlüssel geladen/erzeugt, Test-Installer
    // umgeschaltet) und dabei den zuletzt gesetzten Busy-Zustand kennen muss.
    private bool _isBusy;

    private readonly string _productVersion;

    // Fensterbreite folgt dem Reiter-Zustand (Nutzerkorrektur 15.08.2026, sechste Runde): nicht
    // nur der Inhalt, das ganze Fenster ist eingeklappt schmal - siehe
    // UpdateWindowWidthForTabState und den Kommentar am Fensteranfang in MainWindow.xaml.
    private const double CollapsedWindowWidth = 540;
    private const double ExpandedWindowWidth = 1000;

    public MainWindow()
    {
        InitializeComponent();

        // Kein ProjectReference auf HaelpMi.Core (siehe .csproj), daher hier lokal statt
        // LiveIdentityFactory.CurrentProgramVersion.
        _productVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        VersionText.Text = $"v{_productVersion}";

        var settings = InstallCreatorSettings.Load();
        VaultwardenServerBox.Text = settings.VaultwardenServerUrl;
        VaultwardenEmailBox.Text = settings.VaultwardenEmail;
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

    // --- Vaultwarden / Update-Signatur (Nutzerwunsch 13.08.2026, "Update-Ei") ---------------

    // Reiter-Umschaltung nach tomedo-Vorbild (Nutzerwunsch 14.08.2026, vierte Runde):
    // Vaultwarden-Karte und Protokoll teilen sich eine Zelle, es ist immer höchstens eins von
    // beiden sichtbar - Öffnen des einen klappt das andere automatisch ein. Erneutes Klicken auf
    // den gerade aktiven Reiter klappt ihn wieder ein (dann ist der Inhaltsbereich leer, genau
    // wie im tomedo-Referenzbild). Fokus aufs Master-Passwort-Feld passiert beim Öffnen der
    // Vaultwarden-Karte, weil sie vorher gar nicht sichtbar ist.
    private void VaultwardenTabButton_Click(object sender, RoutedEventArgs e)
    {
        var show = VaultwardenPanel.Visibility != Visibility.Visible;
        VaultwardenPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LogPanel.Visibility = Visibility.Collapsed;
        UpdateWindowWidthForTabState();

        // Fokus nur, wenn diese Sitzung noch nicht entsperrt hat (Nutzerkorrektur 15.08.2026,
        // fünfte Runde) - VaultwardenLockedPanel/VaultwardenUnlockedPanel behalten ihren
        // Zustand automatisch übers Ein-/Ausklappen des Reiters hinweg, ein Fokusversuch auf
        // das dann versteckte Passwortfeld wäre unnötig (harmloses No-op, aber unsauber).
        if (show && VaultwardenLockedPanel.Visibility == Visibility.Visible)
        {
            VaultwardenPasswordBox.Focus();
        }
    }

    // Nicht nur der Inhalt, das ganze Fenster klappt mit ein (Nutzerkorrektur 15.08.2026, sechste
    // Runde) - siehe Kommentar am Fensteranfang in MainWindow.xaml. Left wird um die halbe
    // Breitendifferenz verschoben, damit das Fenster beim Wachsen/Schrumpfen horizontal zentriert
    // bleibt statt nur nach rechts zu wandern.
    private void UpdateWindowWidthForTabState()
    {
        var anyPanelOpen = VaultwardenPanel.Visibility == Visibility.Visible || LogPanel.Visibility == Visibility.Visible;
        var targetWidth = anyPanelOpen ? ExpandedWindowWidth : CollapsedWindowWidth;
        if (targetWidth == Width)
        {
            return;
        }

        Left -= (targetWidth - Width) / 2;
        Width = targetWidth;
    }

    // Grundeinstellungen (Server-URL/E-Mail) hinter dem Zahnrad - reines Ein-/Ausblenden,
    // unabhängig vom gesperrt/entsperrt-Zustand darunter (Nutzerwunsch 15.08.2026, fünfte Runde).
    private void VaultwardenSettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        VaultwardenBasicSettingsPanel.Visibility = VaultwardenBasicSettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void LogsTabButton_Click(object sender, RoutedEventArgs e)
    {
        var show = LogPanel.Visibility != Visibility.Visible;
        LogPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        VaultwardenPanel.Visibility = Visibility.Collapsed;
        UpdateWindowWidthForTabState();
    }

    private async void VaultwardenUnlockButton_Click(object sender, RoutedEventArgs e)
    {
        var serverUrl = VaultwardenServerBox.Text.Trim();
        var email = VaultwardenEmailBox.Text.Trim();
        var password = VaultwardenPasswordBox.Password;

        if (serverUrl.Length == 0 || email.Length == 0 || password.Length == 0)
        {
            VaultwardenLockedStatusText.Text = "Server-URL, E-Mail und Master-Passwort ausfüllen.";
            return;
        }

        _vaultwardenClient ??= VaultwardenClient.TryCreate();
        if (_vaultwardenClient is null)
        {
            VaultwardenLockedStatusText.Text = "bw.exe (Bitwarden-CLI) nicht im PATH gefunden - Update-Signierung ohne sie nicht möglich.";
            Log("Fehler: bw.exe nicht gefunden.");
            return;
        }

        VaultwardenUnlockButton.IsEnabled = false;
        var originalButtonText = VaultwardenUnlockButton.Content;
        VaultwardenUnlockButton.Content = "Verbindet...";
        try
        {
            var unlock = await _vaultwardenClient.UnlockAsync(serverUrl, email, password);
            // Passwort wird in keinem Fall weiter gebraucht/gemerkt - unabhängig vom Ausgang leeren.
            VaultwardenPasswordBox.Password = string.Empty;

            if (!unlock.Ok)
            {
                VaultwardenLockedStatusText.Text = $"Fehlgeschlagen: {unlock.Error}";
                Log($"Vaultwarden-Entsperren fehlgeschlagen: {unlock.Error}");
                return;
            }

            _vaultSessionKey = unlock.SessionKey;
            new InstallCreatorSettings { VaultwardenServerUrl = serverUrl, VaultwardenEmail = email }.Save();
            Log("Vaultwarden entsperrt.");

            // Ab hier "entsperrt" (Nutzerwunsch 15.08.2026, fünfte Runde) - unabhängig davon,
            // ob schon ein Schlüssel existiert; das Status-Icon unten zeigt das getrennt an.
            VaultwardenLockedPanel.Visibility = Visibility.Collapsed;
            VaultwardenUnlockedPanel.Visibility = Visibility.Visible;

            // Beide Schlüssel werden bei jedem Entsperren geladen (Nutzerwunsch 16.08.2026:
            // "beide werden bei Boot/Passworteingabe geladen") - unabhängig davon, ob der
            // gerade angehakte Test-Installer-Zustand den einen oder anderen überhaupt braucht.
            await LoadKeyStatusAsync(VaultwardenClient.UpdatePrivateKeyItemName, isTest: false);
            await LoadKeyStatusAsync(VaultwardenClient.UpdateTestPrivateKeyItemName, isTest: true);

            GenerateUpdateKeyButton.IsEnabled = true;
            GenerateTestUpdateKeyButton.IsEnabled = true;
            UpdateBuildButtonsEnabledState();
        }
        catch (Exception ex)
        {
            Log($"Unerwarteter Fehler beim Vaultwarden-Zugriff: {ex.Message}");
            CrashLogger.Log("VaultwardenUnlockButton_Click", ex);
            VaultwardenLockedStatusText.Text = "Unerwarteter Fehler - siehe Protokoll.";
        }
        finally
        {
            VaultwardenUnlockButton.IsEnabled = true;
            VaultwardenUnlockButton.Content = originalButtonText;
        }
    }

    /// <summary>Lädt eine der beiden Schlüssel-Notizen und spiegelt das Ergebnis in Feld +
    /// Status-Icon/-Text der jeweils passenden UI-Zeile (Produktiv oder Test).</summary>
    private async Task LoadKeyStatusAsync(string itemName, bool isTest)
    {
        var existingKey = await _vaultwardenClient!.TryGetUpdatePrivateKeyAsync(_vaultSessionKey!, itemName);
        var keyBytes = existingKey is not null ? Convert.FromBase64String(existingKey) : null;

        if (isTest)
        {
            _updateTestPrivateKeyBytes = keyBytes;
            TestKeyPresentIcon.Visibility = keyBytes is not null ? Visibility.Visible : Visibility.Collapsed;
            TestKeyMissingIcon.Visibility = keyBytes is not null ? Visibility.Collapsed : Visibility.Visible;
            VaultwardenTestStatusText.Text = keyBytes is not null
                ? "Testschlüssel aus Vaultwarden geladen."
                : "Noch kein Test-Key vorhanden - Rotier-Knopf nutzen.";
        }
        else
        {
            _updatePrivateKeyBytes = keyBytes;
            KeyPresentIcon.Visibility = keyBytes is not null ? Visibility.Visible : Visibility.Collapsed;
            KeyMissingIcon.Visibility = keyBytes is not null ? Visibility.Collapsed : Visibility.Visible;
            VaultwardenStatusText.Text = keyBytes is not null
                ? "Schlüssel aus Vaultwarden geladen."
                : "Noch kein Schlüssel vorhanden - Rotier-Knopf nutzen.";
        }

        if (keyBytes is not null)
        {
            Log(isTest ? "Test-Signaturschlüssel aus Vaultwarden geladen." : "Update-Signaturschlüssel aus Vaultwarden geladen.");
        }
    }

    private async void GenerateUpdateKeyButton_Click(object sender, RoutedEventArgs e) =>
        await RegenerateKeyAsync(VaultwardenClient.UpdatePrivateKeyItemName, isTest: false);

    private async void GenerateTestUpdateKeyButton_Click(object sender, RoutedEventArgs e) =>
        await RegenerateKeyAsync(VaultwardenClient.UpdateTestPrivateKeyItemName, isTest: true);

    /// <summary>Gemeinsame Rotier-Logik für Produktiv- UND Test-Schlüssel (Nutzerwunsch
    /// 16.08.2026: "1zu1 gleiche Schaltfläche wie 'Neuer Schlüssel', nur für 'Neuen Test-Key
    /// erzeugen'") - Archivieren-vor-Überschreiben/Rollback-Verhalten bleibt exakt wie bisher,
    /// nur welche Notiz/welches Feld/welche Status-UI betroffen ist, hängt von
    /// <paramref name="isTest"/> ab.</summary>
    private async Task RegenerateKeyAsync(string itemName, bool isTest)
    {
        if (_vaultwardenClient is null || _vaultSessionKey is null)
        {
            return; // Buttons sind ohne entsperrte Session ohnehin deaktiviert
        }

        var keyLabel = isTest ? "Test-Schlüssel" : "Schlüssel";
        var currentKeyBytes = isTest ? _updateTestPrivateKeyBytes : _updatePrivateKeyBytes;
        var statusText = isTest ? VaultwardenTestStatusText : VaultwardenStatusText;
        var presentIcon = isTest ? TestKeyPresentIcon : KeyPresentIcon;
        var missingIcon = isTest ? TestKeyMissingIcon : KeyMissingIcon;
        var button = isTest ? GenerateTestUpdateKeyButton : GenerateUpdateKeyButton;

        if (currentKeyBytes is not null)
        {
            var confirm = System.Windows.MessageBox.Show(
                $"Es liegt bereits ein {keyLabel} in Vaultwarden. Der alte Schlüssel wird nicht gelöscht, " +
                "sondern unter einem neuen Namen (\"...-deprecated-<Zeitstempel>\") archiviert - die Notiz " +
                "selbst bleibt vollständig erhalten und lässt sich in Vaultwarden jederzeit nachlesen. " +
                "Trotzdem macht ein neuer Schlüssel alle bisher ausgelieferten öffentlichen Schlüssel " +
                $"ungültig - Geräte, die den alten eingebettet haben, können künftige Updates dann nicht " +
                $"mehr verifizieren, bis sie den neuen (manuell) erhalten. Wirklich einen neuen {keyLabel} erzeugen?",
                "HälpMi Install-Creator", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        // Beide Rotier-Knöpfe sperren, nicht nur den geklickten - bw.exe ist ein einzelner
        // externer Prozess pro Aufruf, ein gleichzeitiger zweiter Lauf (Klick auf den jeweils
        // anderen Knopf mitten in diesem hier) könnte sich sonst mit diesem Ablauf überschneiden.
        GenerateUpdateKeyButton.IsEnabled = false;
        GenerateTestUpdateKeyButton.IsEnabled = false;
        try
        {
            // Unbedingt archivieren, nicht ans lokale Feld gekoppelt (Nutzerwunsch 15.08.2026,
            // fünfte Runde) - falls seit dem Entsperren dieser Sitzung schon anderswo ein
            // Schlüssel unter dem Standardnamen angelegt wurde, den diese Sitzung lokal noch
            // nicht kennt, verhindert das eine zweite Notiz mit demselben Namen. Kein
            // bestehender Schlüssel = no-op (Ok=true, DeprecatedName=null).
            var archive = await _vaultwardenClient.ArchiveExistingKeyIfPresentAsync(_vaultSessionKey, itemName);
            if (!archive.Ok)
            {
                Log($"Fehler: alter {keyLabel} konnte nicht archiviert werden - neuer {keyLabel} wird NICHT erzeugt.");
                statusText.Text = "Archivierung fehlgeschlagen - siehe Protokoll. Alter Schlüssel bleibt unverändert.";
                return;
            }
            if (archive.DeprecatedName is not null)
            {
                Log($"Alter {keyLabel} archiviert unter \"{archive.DeprecatedName}\".");
            }

            var keyPair = UpdateSigningOperations.GenerateKeyPair();
            var pushed = await _vaultwardenClient.CreateUpdatePrivateKeyNoteAsync(_vaultSessionKey, itemName, Convert.ToBase64String(keyPair.PrivateKey));
            if (!pushed)
            {
                Log($"Fehler: Neuer {keyLabel} konnte nicht in Vaultwarden gespeichert werden.");
                if (archive.DeprecatedName is not null)
                {
                    var restored = await _vaultwardenClient.RestoreArchivedKeyAsync(_vaultSessionKey, archive.DeprecatedName, itemName);
                    Log(restored
                        ? "Rollback erfolgreich - alter Schlüssel liegt wieder unter dem Standardnamen."
                        : $"Rollback fehlgeschlagen - alter Schlüssel liegt weiterhin unter \"{archive.DeprecatedName}\", bitte manuell in Vaultwarden zurückbenennen.");
                }
                statusText.Text = "Schlüsselerzeugung fehlgeschlagen - siehe Protokoll.";
                return;
            }

            if (isTest)
            {
                _updateTestPrivateKeyBytes = keyPair.PrivateKey;
            }
            else
            {
                _updatePrivateKeyBytes = keyPair.PrivateKey;
            }
            var publicKeyBase64 = Convert.ToBase64String(keyPair.PublicKey);

            Log($"Neuer {(isTest ? "Test-Signaturschlüssel" : "Update-Signaturschlüssel")} erzeugt und in Vaultwarden gespeichert.");
            if (!isTest)
            {
                // Der Test-Key braucht keinen Eintrag in UpdateSignaturePublicKey.cs - der
                // öffentliche Teil wird pro Build aus dem privaten Schlüssel abgeleitet und
                // direkt in deployment.json geschrieben (siehe BuildAdminInstallerAsync),
                // nicht als kompilierter Fallback im Repo gepflegt.
                Log($"OEFFENTLICHER Schlüssel (in HaelpMi.Core/Updates/UpdateSignaturePublicKey.cs eintragen): {publicKeyBase64}");
                TryCopyToClipboard(publicKeyBase64);
            }

            presentIcon.Visibility = Visibility.Visible;
            missingIcon.Visibility = Visibility.Collapsed;
            statusText.Text = isTest
                ? "Neuer Test-Key gespeichert."
                : "Neuer Schlüssel gespeichert. Öffentlicher Schlüssel wurde kopiert - bitte manuell in UpdateSignaturePublicKey.cs eintragen.";
            UpdateBuildButtonsEnabledState();
        }
        catch (Exception ex)
        {
            Log($"Unerwarteter Fehler bei der Schlüsselerzeugung: {ex.Message}");
            CrashLogger.Log("RegenerateKeyAsync", ex);
        }
        finally
        {
            GenerateUpdateKeyButton.IsEnabled = true;
            GenerateTestUpdateKeyButton.IsEnabled = true;
        }
    }

    // Bewusst kein Retry/Rückmeldung über MessageBox hier wie bei CopyPasswordButton_Click -
    // das dortige aufwendige Retry-Muster ist für ein Passwort gedacht, das der Nutzer aktiv
    // weitergeben will; hier reicht best-effort, der Wert steht ohnehin auch im Protokoll.
    private static void TryCopyToClipboard(string text)
    {
        try
        {
            System.Windows.Forms.Clipboard.SetDataObject(text, copy: true, retryTimes: 10, retryDelay: 100);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // best-effort - der Wert steht zusätzlich im Protokoll, siehe Aufrufer
        }
    }

    private async void PublishUpdatePackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updatePrivateKeyBytes is null)
        {
            return; // Button ist ohne geladenen Schlüssel ohnehin deaktiviert
        }

        SetBusy(true);
        PublishUpdatePackageButton.IsEnabled = false;
        try
        {
            Log("--- Update-Paket wird veröffentlicht ---");
            var installerDir = FindInstallerDirectory();

            if (!await RefreshPayloadAsync(installerDir))
            {
                Log("Payload-Aktualisierung fehlgeschlagen - Update-Paket wird nicht veröffentlicht.");
                return;
            }

            var payloadDir = Path.Combine(installerDir, "payload");
            var result = UpdatePackageBuilder.Build(payloadDir, _productVersion, _updatePrivateKeyBytes);
            UpdatePackageBuilder.WriteToPayloadSeed(installerDir, result);
            Log($"Update-Paket für Version {_productVersion} signiert und nach installer/payload/update-seed/ geschrieben.");

            if (UpdatePackageBuilder.TryWriteToLocalDeviceCache(_productVersion, result))
            {
                Log("Zusätzlich in den lokalen P2P-Cache dieser Maschine geschrieben (%ProgramData%\\HaelpMi\\updates-cache) - " +
                    "dieses Gerät kann die Version jetzt sofort an Peers weiterverteilen, sobald sie im Admin-Dashboard freigegeben ist.");
            }
            else
            {
                Log("HälpMi ist auf dieser Maschine nicht installiert - lokaler Cache wurde nicht befüllt (nur der Installer-Payload).");
            }
        }
        catch (Exception ex)
        {
            Log($"Unerwarteter Fehler: {ex.Message}");
            CrashLogger.Log("PublishUpdatePackageButton_Click", ex);
            System.Windows.MessageBox.Show($"Update-Paket-Veröffentlichung fehlgeschlagen:{Environment.NewLine}{ex.Message}",
                "HälpMi Install-Creator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            PublishUpdatePackageButton.IsEnabled = _updatePrivateKeyBytes is not null;
        }
    }

    private async void CreateUpdateBootstrapperButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updatePrivateKeyBytes is null)
        {
            return; // Button ist ohne geladenen Schlüssel ohnehin deaktiviert
        }

        SetBusy(true);
        CreateUpdateBootstrapperButton.IsEnabled = false;
        try
        {
            Log("--- Update wird erstellt ---");
            var outputExePath = await BuildUpdateBootstrapperAsync();
            if (outputExePath is not null)
            {
                Log("Diese eine Datei geht an den Admin - Doppelklick dort aktualisiert die Maschine sofort selbst " +
                    "und macht die Version im \"Updates\"-Tab des Dashboards zur Freigabe verfügbar.");
                ShowUpdateSuccessToast(outputExePath);
            }
        }
        catch (Exception ex)
        {
            Log($"Unerwarteter Fehler: {ex.Message}");
            CrashLogger.Log("CreateUpdateBootstrapperButton_Click", ex);
            System.Windows.MessageBox.Show($"Update-Erstellung fehlgeschlagen:{Environment.NewLine}{ex.Message}",
                "HälpMi Install-Creator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            CreateUpdateBootstrapperButton.IsEnabled = _updatePrivateKeyBytes is not null;
        }
    }

    /// <summary>
    /// Baut die einzelne, eigenständig lauffähige Self-Bootstrap-Update-Datei (Nutzerwunsch
    /// 16.08.2026) - signiertes Paket + veröffentlichtes HaelpMi.UpdateBootstrapper an eine
    /// Datei angehängt (siehe UpdatePackageBuilder.AppendUpdatePackage). Gibt den fertigen
    /// Dateipfad zurück (oder null bei einem Fehler, bereits geloggt).
    ///
    /// Bewusst getrennt vom Klick-Handler und ohne jeden UI-Zugriff außer Log(...): eine
    /// spätere Auto-Publish-Erweiterung (Nutzerwunsch, noch nicht umgesetzt - "Update-Paket
    /// automatisch per hinterlegter Mail an alle hinterlegten Kunden verschicken") kann
    /// diese Methode direkt aufrufen, ohne einen Button-Klick zu simulieren. Der
    /// Rückgabewert (fertiger, deterministisch benannter Dateipfad unter installer/Output/)
    /// ist bereits genau das, was ein künftiger Versand-Schritt bräuchte - hier absichtlich
    /// noch kein Kundenregister/Mailversand/SMTP eingebaut, nur der Weg dahin nicht verbaut.
    /// </summary>
    private async Task<string?> BuildUpdateBootstrapperAsync()
    {
        var installerDir = FindInstallerDirectory();

        if (!await RefreshPayloadAsync(installerDir))
        {
            Log("Payload-Aktualisierung fehlgeschlagen - Update wird nicht erstellt.");
            return null;
        }

        var payloadDir = Path.Combine(installerDir, "payload");
        var result = UpdatePackageBuilder.Build(payloadDir, _productVersion, _updatePrivateKeyBytes!);
        Log($"Update-Paket für Version {_productVersion} signiert.");

        var repoRoot = Directory.GetParent(installerDir)?.FullName;
        if (repoRoot is null)
        {
            Log("Fehler: Repo-Wurzel (oberhalb von installer/) konnte nicht bestimmt werden.");
            return null;
        }

        var bootstrapperProject = Path.Combine(repoRoot, "src", "HaelpMi.UpdateBootstrapper", "HaelpMi.UpdateBootstrapper.csproj");
        if (!File.Exists(bootstrapperProject))
        {
            Log($"Fehler: {bootstrapperProject} nicht gefunden.");
            return null;
        }

        // Immer frisch veröffentlichen statt eine frühere Kopie wiederzuverwenden - gleiches
        // Prinzip wie RefreshPayloadAsync (Bugfix 11.08.2026: ein tagealter, still
        // veralteter Payload darf nie stillschweigend weiterverwendet werden).
        var publishDir = Path.Combine(installerDir, "UpdateBootstrapperPublish");
        if (Directory.Exists(publishDir))
        {
            Directory.Delete(publishDir, true);
        }

        Log("Update-Bootstrap-Werkzeug wird veröffentlicht (Single-File, self-contained)...");
        var exitCode = await RunProcessAsync("dotnet", repoRoot, args =>
        {
            args.Add("publish");
            args.Add(bootstrapperProject);
            args.Add("-c");
            args.Add("Release");
            args.Add("-r");
            args.Add("win-x64");
            args.Add("-p:Platform=x64");
            args.Add("--self-contained");
            args.Add("true");
            args.Add("-p:PublishSingleFile=true");
            args.Add("-o");
            args.Add(publishDir);
        }, "[dotnet publish] ");

        if (exitCode != 0)
        {
            Log($"Fehler: dotnet publish für HaelpMi.UpdateBootstrapper fehlgeschlagen (Exitcode {exitCode}).");
            return null;
        }

        var genericExePath = Path.Combine(publishDir, "HaelpMi.UpdateBootstrapper.exe");
        if (!File.Exists(genericExePath))
        {
            Log($"Fehler: {genericExePath} fehlt nach dem Publish.");
            return null;
        }

        var outputDir = Path.Combine(installerDir, "Output");
        Directory.CreateDirectory(outputDir);
        var outputExePath = Path.Combine(outputDir, $"HaelpMi-Update-{_productVersion}.exe");
        UpdatePackageBuilder.AppendUpdatePackage(genericExePath, outputExePath, result);
        Log($"Update erstellt: {outputExePath}");

        return outputExePath;
    }

    private async void BuildAdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidate(out var error))
        {
            System.Windows.MessageBox.Show(error, "HälpMi Install-Creator", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var customerGroupId = Guid.NewGuid(); // FR-49: fest für dieses Admin-Installer-Paket und jeden späteren daraus exportierten User-Installer
        // LAN-Verschlüsselung (siehe CLAUDE.md "Lizenz & Secrets", SecureEnvelopeCodec):
        // gruppenweiter symmetrischer Schlüssel, an derselben Stelle wie customerGroupId
        // erzeugt und über denselben Weg (deployment.json in beiden Installer-Varianten)
        // eingebettet - nie über das Netzwerk übertragen, nie hier geloggt.
        var groupKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        // Admin-Rollen-Signatur (Nutzerwunsch 17.08.2026, viertes Schlüsselpaar, CLAUDE.md
        // "Lizenz & Secrets"): pro Kunden-Gruppe neu, an derselben Stelle wie
        // customerGroupId/groupKeyBase64 erzeugt (jeder Klick auf "Installer erstellen"
        // mintet ohnehin eine komplett neue, unabhängige Kundengruppe - kein Rebuild-für-
        // denselben-Kunden-Fall in diesem Tool, siehe customerGroupId-Kommentar oben).
        var adminRoleKeyPair = AdminRoleKeyGenerator.GenerateKeyPair();
        var password = PasswordBox.Password;
        var isTestInstaller = TestInstallerCheckBox.IsChecked == true;
        var customerNameOrTestLabel = CustomerNameBox.Text.Trim();

        SetBusy(true);
        try
        {
            await BuildAdminInstallerAsync(customerGroupId, groupKeyBase64, adminRoleKeyPair, isTestInstaller, password, customerNameOrTestLabel);
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

        // Nutzerwunsch 16.08.2026: Admin-Installer braucht jetzt zwingend einen geladenen
        // Schlüssel, auch im Test - Test-Installer nutzt dafür den separaten Test-Key statt
        // des Produktiv-Schlüssels (siehe BuildAdminInstallerAsync). IsEnabled auf
        // BuildAdminButton (siehe UpdateBuildButtonsEnabledState) deckt den Normalfall schon
        // über die Grau/Blau-Färbung ab - diese Prüfung ist das Sicherheitsnetz, falls der
        // Klick trotzdem durchkommt (z. B. veralteter IsEnabled-Zustand).
        var requiredKeyLoaded = isTest ? _updateTestPrivateKeyBytes is not null : _updatePrivateKeyBytes is not null;
        if (!requiredKeyLoaded)
        {
            error = isTest
                ? "Kein Test-Key geladen - ein Test-Installer kann ohne ihn nicht gebaut werden (siehe \"Vaultwarden\"-Reiter rechts)."
                : "Kein Update-Schlüssel geladen - der Admin-Installer kann ohne ihn nicht gebaut werden (siehe \"Vaultwarden\"-Reiter rechts).";
            return false;
        }

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
        _isBusy = busy;
        TestInstallerCheckBox.IsEnabled = !busy;
        CustomerNameBox.IsEnabled = !busy;
        UpdateBuildButtonsEnabledState();
    }

    /// <summary>
    /// Zentrale Grau/Blau-Logik für alle drei Build-Knöpfe (Nutzerwunsch 16.08.2026: "grau
    /// wenn nicht klickbar, blau wenn klickbar", siehe PrimaryActionButtonStyle in
    /// ModernStyles.xaml) - aufgerufen von SetBusy, nach jedem Laden/Erzeugen eines
    /// Schlüssels und beim Umschalten von TestInstallerCheckBox, weil BuildAdminButton je
    /// nach dessen Zustand einen ANDEREN Schlüssel braucht (Test-Key vs. Produktiv-Key).
    /// PublishUpdatePackageButton/CreateUpdateBootstrapperButton bauen immer ein echtes
    /// Produktiv-Update, unabhängig vom Test-Installer-Häkchen - brauchen daher immer den
    /// Produktiv-Schlüssel.
    /// </summary>
    private void UpdateBuildButtonsEnabledState()
    {
        var isTest = TestInstallerCheckBox.IsChecked == true;
        var requiredKeyLoaded = isTest ? _updateTestPrivateKeyBytes is not null : _updatePrivateKeyBytes is not null;

        BuildAdminButton.IsEnabled = !_isBusy && requiredKeyLoaded;
        // PublishUpdatePackageButton/CreateUpdateBootstrapperButton greifen auf denselben
        // installer/payload/-Ordner zu wie RefreshPayloadAsync - während eines Baus (egal
        // welcher der drei Aktionen) darf keiner der anderen Wege gleichzeitig hineinschreiben.
        PublishUpdatePackageButton.IsEnabled = !_isBusy && _updatePrivateKeyBytes is not null;
        CreateUpdateBootstrapperButton.IsEnabled = !_isBusy && _updatePrivateKeyBytes is not null;
    }

    private void TestInstallerCheckBox_Click(object sender, RoutedEventArgs e) => UpdateBuildButtonsEnabledState();

    private async Task BuildAdminInstallerAsync(Guid customerGroupId, string groupKeyBase64, AdminRoleKeyGenerator.KeyPair adminRoleKeyPair, bool isTestInstaller, string password, string customerNameOrTestLabel)
    {
        Log("--- Installer werden erstellt ---");
        Log($"Kunden-Gruppen-ID: {customerGroupId}");
        Log("Gruppenschlüssel (LAN-Verschlüsselung) wurde erzeugt."); // Wert selbst landet nie im Log, siehe Kommentar bei der Erzeugung
        // Nur der öffentliche Teil landet im Protokoll - der private Teil wird nie geloggt,
        // nie zwischengelagert (siehe AdminRoleKeyGenerator-Klassendoku).
        Log($"Admin-Rollen-Schlüssel erzeugt (öffentlicher Teil: {adminRoleKeyPair.PublicKeyBase64}).");
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

        // Startpaket-Einbettung (bis 15.08.2026 hinter der "Update-Ei"-Checkbox, seit
        // 16.08.2026 immer automatisch - Nutzerwunsch: kostet nichts, wenn es immer dabei
        // ist). NACH RefreshPayloadAsync (frischer Payload-Ordner), VOR dem
        // User-Installer-ISCC-Lauf unten - der kopiert "payload\*" 1:1 in beide Installer,
        // update-seed/ muss also schon drinstehen, bevor ISCC läuft.
        //
        // Nutzerwunsch 16.08.2026: kein Soft-Skip mehr - Admin-Installer braucht jetzt
        // zwingend einen geladenen Schlüssel (siehe TryValidate, hat das schon vorher
        // sichergestellt). Test-Installer signiert mit dem separaten Test-Key statt dem
        // Produktiv-Schlüssel, damit Test- und Produktiv-Kreise beim Signaturcheck nie
        // ineinanderlaufen können (siehe DeploymentInfo.UpdatePublicKeyBase64-Kommentar).
        var signingKey = isTestInstaller ? _updateTestPrivateKeyBytes : _updatePrivateKeyBytes;
        if (signingKey is null)
        {
            Log($"Fehler: kein {(isTestInstaller ? "Test-Key" : "Update-Schlüssel")} geladen - Installer wird nicht gebaut.");
            return;
        }

        Log($"Update-Paket wird für diesen Build signiert und eingebettet ({(isTestInstaller ? "Test-Key" : "Produktiv-Schlüssel")})...");
        var payloadDir = Path.Combine(installerDir, "payload");
        var eggResult = UpdatePackageBuilder.Build(payloadDir, _productVersion, signingKey);
        UpdatePackageBuilder.WriteToPayloadSeed(installerDir, eggResult);
        Log($"Update-Paket: Version {_productVersion} signiert, landet in payload/update-seed/.");

        // Der zum Signierschlüssel passende öffentliche Schlüssel wird aus ihm abgeleitet
        // (Ed25519: der öffentliche Teil ist aus dem privaten deterministisch berechenbar,
        // siehe UpdateSigningOperations.DerivePublicKey) und unten per ISCC-Define in
        // deployment.json JEDER aus diesem Lauf gebauten Installation eingebettet - jede
        // Installation kennt/vertraut dadurch nur dem für sie relevanten Schlüssel.
        var updatePublicKeyBase64 = Convert.ToBase64String(UpdateSigningOperations.DerivePublicKey(signingKey));

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
            args.Add($"/DGroupKeyBase64={groupKeyBase64}");
            args.Add($"/DIsTestInstaller={(isTestInstaller ? "true" : "false")}");
            args.Add($"/DUpdatePublicKeyBase64={updatePublicKeyBase64}");
            // Öffentlicher Admin-Rollen-Schlüssel geht in BEIDE Installer-Varianten - jedes
            // Gerät, Admin wie User, muss Admin-Behauptungen anderer Geräte prüfen können.
            // Der private Teil geht bewusst NICHT hierher (nur unten, Admin-Installer).
            args.Add($"/DAdminRolePublicKeyBase64={adminRoleKeyPair.PublicKeyBase64}");
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
            args.Add($"/DGroupKeyBase64={groupKeyBase64}");
            args.Add($"/DIsTestInstaller={(isTestInstaller ? "true" : "false")}");
            args.Add($"/DUpdatePublicKeyBase64={updatePublicKeyBase64}");
            args.Add($"/DAdminRolePublicKeyBase64={adminRoleKeyPair.PublicKeyBase64}");
            // Privater Teil NUR hier - gleiches Muster wie InstallerPassword unten (nur der
            // Admin-Installer bekommt ihn übergeben, nie der User-Installer-Aufruf oben).
            args.Add($"/DAdminRolePrivateKeyBase64={adminRoleKeyPair.PrivateKeyBase64}");
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
            LastBuildTitleText.Text = "Admin-Installer erstellt";
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

    // Gleiches Panel/Toast-Muster wie ShowSuccessToast oben, nur ohne "neueste .exe im
    // Ordner suchen" - beim Update-Erstellen kennen wir den fertigen Pfad schon exakt.
    private void ShowUpdateSuccessToast(string exePath)
    {
        try
        {
            _lastBuiltInstallerPath = exePath;
            var fileName = Path.GetFileName(exePath);
            LastBuildTitleText.Text = "Update erstellt";
            LastBuildFileText.Text = fileName + " liegt bereit für den Admin.";
            LastBuildPanel.Visibility = Visibility.Visible;

            var toast = new BuildSuccessToastWindow("Update erstellt", fileName + " liegt bereit für den Admin.", exePath);
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
