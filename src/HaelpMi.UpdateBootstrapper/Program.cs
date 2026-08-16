using System.Text.Json;
using HaelpMi.Core.Ipc;
using HaelpMi.Core.Storage;
using HaelpMi.Core.Updates;

namespace HaelpMi.UpdateBootstrapper;

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
    private static async Task<int> Main()
    {
        Console.WriteLine("HälpMi Update-Bootstrap");
        Console.WriteLine("========================");
        Console.WriteLine();

        bool success;
        try
        {
            success = await RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Unerwarteter Fehler: {ex.Message}");
            success = false;
        }

        Console.WriteLine();
        Console.WriteLine(success
            ? "Fertig. Taste drücken zum Schließen..."
            : "Abgebrochen (siehe Fehlermeldung oben). Taste drücken zum Schließen...");
        Console.ReadKey(intercept: true);
        return success ? 0 : 1;
    }

    private static async Task<bool> RunAsync()
    {
        Console.WriteLine("Eingebettetes Update-Paket wird gelesen...");
        var extracted = EmbeddedPackage.TryExtract();
        if (extracted is null)
        {
            Console.WriteLine("Fehler: Diese Datei enthält kein eingebettetes Update-Paket - bitte über " +
                "Install-Creator > \"Update erstellen\" bauen, nicht die rohe .exe verwenden.");
            return false;
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
            Console.WriteLine("Fehler: eingebettetes manifest.json ist beschädigt.");
            return false;
        }

        Console.WriteLine($"Version im Paket: {manifest.Version}");
        Console.WriteLine("Signatur wird geprüft...");
        if (!UpdatePackageVerifier.Verify(packageZip, manifest))
        {
            Console.WriteLine("Fehler: Signaturprüfung fehlgeschlagen - Paket wird NICHT installiert.");
            return false;
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
            Console.WriteLine("Läuft der HälpMi-Update-Dienst auf dieser Maschine? Ist HälpMi hier überhaupt installiert?");
            return false;
        }

        Console.WriteLine("Teste die neue Version testweise auf einem separaten Port (parallel zur laufenden Version)...");
        var testPort = UpdateTestInstancePing.GetEphemeralPort();
        var startTest = await client.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.StartTest, manifest.Version, TestPort: testPort));
        if (!startTest.Success)
        {
            Console.WriteLine($"Fehler beim Selbsttest-Start: {startTest.Error}");
            await client.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.Rollback, manifest.Version));
            return false;
        }

        if (!await UpdateTestInstancePing.PingAsync(testPort))
        {
            Console.WriteLine("Fehler: Selbsttest der neuen Version hat nicht rechtzeitig geantwortet.");
            await client.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.Rollback, manifest.Version));
            return false;
        }
        Console.WriteLine("Selbsttest OK.");

        Console.WriteLine("Übernehme die neue Version (kurze Unterbrechung, dann Neustart)...");
        var swap = await client.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.ConfirmSwap, manifest.Version), TimeSpan.FromMinutes(2));
        if (!swap.Success)
        {
            Console.WriteLine($"Fehler bei der Übernahme: {swap.Error}");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"Diese Maschine läuft jetzt auf Version {manifest.Version}.");
        Console.WriteLine("Nächster Schritt: im Admin-Dashboard, Reiter \"Updates\", diese Version einmal freigeben -");
        Console.WriteLine("ab dann verbreitet sie sich automatisch in Wellen an alle anderen erreichbaren Geräte weiter.");
        return true;
    }
}
