using System.IO;
using System.Text.Json;
using HaelpMi.Core.Ipc;
using HaelpMi.Core.Storage;
using HaelpMi.Core.Updates;

namespace HaelpMi.UpdateBootstrapper;

// System.IO explizit - siehe Kommentar in EmbeddedPackage.cs (UseWPF laesst es aus
// ImplicitUsings weg, "Path" wird hier ausschliesslich als System.IO.Path gebraucht).

/// <summary>
/// Self-Bootstrap-Update (Nutzerwunsch 16.08.2026): schließt die Lücke, dass bisher IMMER
/// ein voller Installer-Lauf nötig war, um die allererste Maschine einer Kundengruppe auf
/// eine neue Version zu bringen, bevor die normale P2P-Kaskade (<see cref="UpdateOrchestrator"/>,
/// Peer-Beobachtung + Wellen-Gate) überhaupt greifen konnte - siehe dessen
/// <c>IsMyTurn</c>-Kommentar: eine Wellenbreite von 0 (noch kein einziger bekannter Peer
/// auf der freigegebenen Version) blockiert dort bewusst jeden Peer-getriebenen Versuch.
/// Genau dieses Tool ist der einzige vorgesehene Weg drumherum.
///
/// Eine einzige, von Install-Creator ("Update erstellen") gebaute .exe, die der Admin per
/// Doppelklick auf einer bereits installierten HälpMi-Maschine ausführt:
/// 1. Liest das an diese exe angehängte, signierte Paket (<see cref="EmbeddedPackage"/>).
/// 2. Prüft die Signatur gegen den eingebetteten öffentlichen Schlüssel.
/// 3. Legt es im lokalen P2P-Cache ab - identisches Dateilayout wie
///    <see cref="UpdatePackageCacheStore"/>, damit es sofort im "Updates"-Tab des
///    Admin-Dashboards auswählbar ist (<c>ListAvailableUpdateVersions</c> liest von dort).
/// 4. Stößt SOFORT die Install-StartTest-ConfirmSwap-Kette gegen den lokal laufenden
///    HaelpMi.UpdateService an - ohne auf einen Peer-Boot-Call zu warten (den es bei der
///    allerersten Maschine noch gar nicht geben kann). Der bewusste menschliche
///    Doppelklick IST hier die Bestätigung, die im normalen P2P-Pfad sonst die
///    Peer-Erreichbarkeits-Bestätigung liefert (siehe UpdateOrchestrator.AttemptUpdateAsync)
///    - kein weiterer Automatismus nötig.
///
/// Danach ist die Freigabe im Admin-Dashboard ("Updates"-Tab) weiterhin ein bewusster,
/// separater Schritt (CLAUDE.md "der Admin gibt das Update genau einmal frei") - dieses
/// Tool bringt nur die allererste Maschine auf die neue Version, es setzt
/// SharedConfig.UpdateRollout.ApprovedVersion nicht selbst.
/// </summary>
internal static class Program
{
    // [STAThread] (seit 19.08.2026, Flaw 14): UpdateFailureWindow braucht einen STA-Thread,
    // wie jedes WPF-Fenster. Funktioniert unverändert mit einem async Main - die Konsole
    // bleibt trotzdem die primäre Ausgabe für den Normalfall, siehe csproj-Kommentar.
    [STAThread]
    private static async Task<int> Main()
    {
        Console.WriteLine("HälpMi Update-Bootstrap");
        Console.WriteLine("========================");
        Console.WriteLine();

        BootstrapFailure? failure;
        try
        {
            failure = await RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Unerwarteter Fehler: {ex.Message}");
            failure = new BootstrapFailure("Unerwarteter Fehler", ex.Message);
        }

        Console.WriteLine();
        if (failure is null)
        {
            // Erfolgsfall unverändert (Flaw 14 betrifft nur den Fehlerfall) - Konsolentext
            // bleibt hier bewusst die einzige Rückmeldung, kein zusätzliches Fenster nötig.
            Console.WriteLine("Fertig. Taste drücken zum Schließen...");
            Console.ReadKey(intercept: true);
            return 0;
        }

        Console.WriteLine("Abgebrochen (siehe Fehlermeldung oben). Details auch im Fenster.");
        // Primäre Nutzer-Ansicht bei Fehlschlag ist jetzt dieses Fenster statt der rohen
        // Konsolenausgabe (Flaw 14) - die Konsole bleibt für Diagnose sichtbar im
        // Hintergrund, blockiert aber nicht mehr auf einen Tastendruck.
        new UpdateFailureWindow(failure.Step, failure.Message, failure.Hint).ShowDialog();
        return 1;
    }

    /// <summary>Ein fehlgeschlagener Schritt der Bootstrap-Kette, für Konsole UND UpdateFailureWindow.</summary>
    private sealed record BootstrapFailure(string Step, string Message, string? Hint = null);

    private static async Task<BootstrapFailure?> RunAsync()
    {
        Console.WriteLine("Eingebettetes Update-Paket wird gelesen...");
        var extracted = EmbeddedPackage.TryExtract();
        if (extracted is null)
        {
            const string message = "Diese Datei enthält kein eingebettetes Update-Paket.";
            const string hint = "Bitte über Install-Creator > \"Update erstellen\" bauen, nicht die rohe .exe verwenden.";
            Console.WriteLine($"Fehler: {message} {hint}");
            return new BootstrapFailure("Update-Paket lesen", message, hint);
        }

        var (manifestJson, packageZip) = extracted.Value;

        UpdatePackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdatePackageManifest>(manifestJson);
        }
        catch (JsonException)
        {
            manifest = null;
        }

        if (manifest is null)
        {
            const string message = "Eingebettetes manifest.json ist beschädigt.";
            Console.WriteLine($"Fehler: {message}");
            return new BootstrapFailure("Update-Paket lesen", message);
        }

        Console.WriteLine($"Version im Paket: {manifest.Version}");
        Console.WriteLine("Signatur wird geprüft...");
        if (!UpdatePackageVerifier.Verify(packageZip, manifest))
        {
            const string message = "Signaturprüfung fehlgeschlagen - Paket wird NICHT installiert.";
            Console.WriteLine($"Fehler: {message}");
            return new BootstrapFailure("Signaturprüfung", message);
        }
        Console.WriteLine("Signatur OK.");

        Console.WriteLine("Wird in den lokalen P2P-Cache dieser Maschine geschrieben...");
        var cacheStore = new UpdatePackageCacheStore();
        cacheStore.Save(manifest.Version, manifest, packageZip);
        var packagePath = Path.Combine(UpdatePackageCacheStore.DirectoryFor(manifest.Version), "package.zip");

        var client = new UpdateServiceIpcClient();

        Console.WriteLine("Installiere...");
        var install = await client.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.Install, manifest.Version, PackageZipPath: packagePath));
        if (!install.Success)
        {
            Console.WriteLine($"Fehler bei der Installation: {install.Error}");
            const string hint = "Läuft der HälpMi-Update-Dienst auf dieser Maschine? Ist HälpMi hier überhaupt installiert?";
            Console.WriteLine(hint);
            return new BootstrapFailure("Installation", install.Error ?? "unbekannter Fehler", hint);
        }

        Console.WriteLine("Teste die neue Version testweise auf einem separaten Port (parallel zur laufenden Version)...");
        var testPort = UpdateTestInstancePing.GetEphemeralPort();
        var startTest = await client.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.StartTest, manifest.Version, TestPort: testPort));
        if (!startTest.Success)
        {
            Console.WriteLine($"Fehler beim Selbsttest-Start: {startTest.Error}");
            await client.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.Rollback, manifest.Version));
            return new BootstrapFailure("Selbsttest-Start", startTest.Error ?? "unbekannter Fehler");
        }

        if (!await UpdateTestInstancePing.PingAsync(testPort))
        {
            const string message = "Selbsttest der neuen Version hat nicht rechtzeitig geantwortet.";
            Console.WriteLine($"Fehler: {message}");
            await client.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.Rollback, manifest.Version));
            return new BootstrapFailure("Selbsttest", message);
        }
        Console.WriteLine("Selbsttest OK.");

        Console.WriteLine("Übernehme die neue Version (kurze Unterbrechung, dann Neustart)...");
        var swap = await client.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.ConfirmSwap, manifest.Version), TimeSpan.FromMinutes(2));
        if (!swap.Success)
        {
            Console.WriteLine($"Fehler bei der Übernahme: {swap.Error}");
            return new BootstrapFailure("Übernahme", swap.Error ?? "unbekannter Fehler");
        }

        Console.WriteLine();
        Console.WriteLine($"Diese Maschine läuft jetzt auf Version {manifest.Version}.");
        Console.WriteLine("Nächster Schritt: im Admin-Dashboard, Reiter \"Updates\", diese Version einmal freigeben -");
        Console.WriteLine("ab dann verbreitet sie sich automatisch in Wellen an alle anderen erreichbaren Geräte weiter.");
        return null;
    }
}
