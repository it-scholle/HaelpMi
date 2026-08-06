using System.Text.Json;
using HaelpMi.Core.Updates;

namespace HaelpMi.Core.Storage;

/// <summary>
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
