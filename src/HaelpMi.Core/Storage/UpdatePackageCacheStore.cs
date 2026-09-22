using System.Text.Json;
using HaelpMi.Core.Updates;
using System.Linq;

namespace HaelpMi.Core.Storage;

/// <summary>
/// DEPRECATED für release-1.0-MVP: bleibt ungefüllt, da <see cref="Updates.UpdateSeedImporter"/>
/// und <see cref="Networking.UpdatePackageDistributionService"/> auf diesem Release-Zweig
/// nicht laufen.
///
/// Lokaler Zwischenspeicher für heruntergeladene/verifizierte Update-Pakete (Abschnitt 11).
/// Jedes Gerät, das eine Version einmal erfolgreich bezogen hat, kann sie darüber auch an
/// andere Peers weiterverteilen ("propagierender Boot-Call") - so muss nicht jedes Gerät
/// vom selben Ursprungsgerät ziehen. Unter %ProgramData%\HaelpMi wie alle anderen
/// Geräte-Daten (Teil 2, FR-34), nicht unter {app} - ein Update-Swap darf diesen Ordner
/// nicht anfassen müssen.
/// </summary>
public sealed class UpdatePackageCacheStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DirectoryFor(string version) =>
        Path.Combine(AppPaths.RootFolder, "updates-cache", version);

    /// <summary>
    /// Nutzerwunsch 09.08.2026 (Admin-Dashboard "Updates"-Tab): welche Versionen dieses
    /// Gerät lokal im Cache hat und damit theoretisch für einen Rollout freigeben könnte -
    /// unabhängig davon, ob es selbst schon auf einer davon läuft (siehe
    /// <see cref="UpdateSeedImporter"/>/<c>UpdateOrchestrator</c>, die hier hineinschreiben).
    /// </summary>
    public List<string> ListAvailableVersions()
    {
        var root = Path.Combine(AppPaths.RootFolder, "updates-cache");
        if (!Directory.Exists(root))
        {
            return new List<string>();
        }

        return Directory.GetDirectories(root)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name) && TryLoad(name!) is not null)
            .Select(name => name!)
            .ToList();
    }

    public (UpdatePackageManifest Manifest, byte[] Payload)? TryLoad(string version)
    {
        var dir = DirectoryFor(version);
        var manifestPath = Path.Combine(dir, "manifest.json");
        var payloadPath = Path.Combine(dir, "package.zip");
        if (!File.Exists(manifestPath) || !File.Exists(payloadPath))
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<UpdatePackageManifest>(File.ReadAllText(manifestPath), Options);
            if (manifest is null)
            {
                return null;
            }

            return (manifest, File.ReadAllBytes(payloadPath));
        }
        catch (Exception)
        {
            return null; // beschädigter Cache-Eintrag - verhält sich wie "nicht vorhanden", kein Absturz
        }
    }

    public void Save(string version, UpdatePackageManifest manifest, byte[] payload)
    {
        var dir = DirectoryFor(version);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest, Options));
        File.WriteAllBytes(Path.Combine(dir, "package.zip"), payload);
    }
}
