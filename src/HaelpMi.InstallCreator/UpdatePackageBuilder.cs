using System.IO;
using System.IO.Compression;
using System.Text;
using HaelpMi.UpdateSigner;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Baut+signiert ein Update-Paket aus einem bereits publizierten Payload-Ordner - gemeinsame
/// Logik für das automatische Einbetten im vollen Admin-Installer-Build (früher "Update-Ei"-
/// Häkchen, seit 16.08.2026 kein separater Schritt mehr) UND den "Update erstellen"-Knopf
/// (CreateUpdateBootstrapperButton). Der frühere schlanke "Update-Paket veröffentlichen"-Knopf
/// (Nutzerwunsch 13.08.2026) ist entfernt (Nutzerwunsch 18.08.2026) - "Update erstellen" deckt
/// den Self-Bootstrap-Fall bereits ab.
///
/// Feste Reihenfolge wichtig: IMMER erst zippen, DANN erst update-seed/ in denselben Ordner
/// schreiben - sonst enthielte das Zip sich beim nächsten Lauf selbst rekursiv.
/// </summary>
public static class UpdatePackageBuilder
{
    public sealed record BuildResult(string ManifestJson, byte[] PackageZip);

    public static BuildResult Build(string payloadDir, string version, byte[] privateKeyBytes)
    {
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"haelpmi-update-{Guid.NewGuid():N}.zip");
        try
        {
            // Inhalte landen direkt im Zip-Root (nicht unter einem "payload/"-Unterordner) -
            // UpdateServiceWorker.InstallAsync erwartet HaelpMi.Agent.exe unmittelbar im
            // ausgepackten Zielordner.
            ZipFile.CreateFromDirectory(payloadDir, tempZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            var packageZip = File.ReadAllBytes(tempZipPath);

            var manifest = UpdateSigningOperations.Sign(packageZip, privateKeyBytes, version);
            var manifestJson = UpdateSigningOperations.ToManifestJson(manifest);

            return new BuildResult(manifestJson, packageZip);
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath); // nur das Zip war je auf der Platte, kein Schlüsselmaterial
            }
        }
    }

    /// <summary>Schreibt manifest.json/package.zip nach installer/payload/update-seed/ - landet
    /// über das bestehende "Source: payload\*" (HaelpMiCommon.iss.inc) automatisch in jedem
    /// künftig gebauten User- UND Admin-Installer, ganz ohne .iss-Änderung.</summary>
    public static void WriteToPayloadSeed(string installerDir, BuildResult result)
    {
        var seedDir = Path.Combine(installerDir, "payload", "update-seed");
        Directory.CreateDirectory(seedDir);
        File.WriteAllText(Path.Combine(seedDir, "manifest.json"), result.ManifestJson);
        File.WriteAllBytes(Path.Combine(seedDir, "package.zip"), result.PackageZip);
    }

    /// <summary>
    /// Schreibt zusätzlich direkt in den lokalen P2P-Cache dieser Maschine
    /// (%ProgramData%\HaelpMi\updates-cache\&lt;version&gt;\), falls HälpMi hier installiert ist -
    /// damit kann diese Maschine sofort als Quelle für andere Geräte dienen, ohne Neuinstallation.
    /// Gleiches Dateilayout wie HaelpMi.Core.Storage.UpdatePackageCacheStore, hier ohne
    /// ProjectReference auf HaelpMi.Core nachgebaut (siehe .csproj-Kommentar) - "HaelpMi" als
    /// Ordnername ist AppConstants.AppDataFolderName dort, hier bewusst als Literal dupliziert.
    ///
    /// TODO-Hinweis (18.08.2026): seit Entfernung des "Update-Paket veröffentlichen"-Knopfs
    /// (dessen einzigem Aufrufer) aktuell ohne Aufrufer im Install-Creator - zur Überarbeitung
    /// markiert statt stillschweigend entfernt, falls die Fähigkeit "lokalen Cache ohne
    /// Neuinstallation befüllen" nicht mehr gebraucht wird, gehört sie ganz weg.
    /// </summary>
    public static bool TryWriteToLocalDeviceCache(string version, BuildResult result)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HaelpMi");
        if (!Directory.Exists(root))
        {
            return false; // HälpMi läuft auf dieser Maschine nicht - nichts zu tun, kein Fehler
        }

        var versionDir = Path.Combine(root, "updates-cache", version);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "manifest.json"), result.ManifestJson);
        File.WriteAllBytes(Path.Combine(versionDir, "package.zip"), result.PackageZip);
        return true;
    }

    /// <summary>
    /// Self-Bootstrap-Update (Nutzerwunsch 16.08.2026, "Update erstellen"-Knopf): kopiert
    /// eine bereits fertig veröffentlichte, generische HaelpMi.UpdateBootstrapper.exe nach
    /// <paramref name="outputExePath"/> und hängt das signierte Update-Paket als Rohdaten
    /// an ihr Ende an - siehe HaelpMi.UpdateBootstrapper/EmbeddedPackage.cs für die
    /// Lese-Seite und die ausführliche Begründung, warum ein simpler Byte-Anhang statt
    /// eines &lt;EmbeddedResource&gt; (vermeidet, echte Paket-Binärdaten im Projektordner/Git
    /// vorhalten zu müssen, nur damit das Bootstrapper-Projekt für sich allein baubar bleibt).
    ///
    /// Footer-Format (MUSS mit EmbeddedPackage.TryExtract synchron gehalten werden):
    /// [package.zip-Bytes][manifest.json-Bytes als UTF-8][8 Byte LE Länge Paket]
    /// [8 Byte LE Länge Manifest][8 ASCII-Byte Magic "HMUBEGG1"].
    /// </summary>
    public static void AppendUpdatePackage(string sourceExePath, string outputExePath, BuildResult result)
    {
        const string magicFooter = "HMUBEGG1";

        File.Copy(sourceExePath, outputExePath, overwrite: true);

        using var stream = new FileStream(outputExePath, FileMode.Append, FileAccess.Write);
        var manifestBytes = Encoding.UTF8.GetBytes(result.ManifestJson);
        stream.Write(result.PackageZip);
        stream.Write(manifestBytes);
        stream.Write(BitConverter.GetBytes((long)result.PackageZip.Length));
        stream.Write(BitConverter.GetBytes((long)manifestBytes.Length));
        stream.Write(Encoding.ASCII.GetBytes(magicFooter));
    }
}
