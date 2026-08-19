using System.Text.Json;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Updates;

/// <summary>
/// Bootstrap für die P2P-Update-Verteilung (Abschnitt 11): "wie kommt das allererste Gerät
/// im Netz zu einer neuen Version" war bisher bewusst außerhalb der P2P-Pipeline (siehe
/// <see cref="UpdateOrchestrator"/>-Klassenkommentar) - reine Handarbeit über
/// <c>HaelpMi.UpdateSigner</c>. Ab jetzt bringt der Installer ein signiertes
/// <c>update-seed\manifest.json</c> + <c>package.zip</c> (derselbe, mit
/// <c>HaelpMi.UpdateSigner sign</c> signierte Release-Build, den auch der Installer selbst
/// enthält) mit. Beim ersten Start nach Install/Update importiert diese Klasse es - sofern
/// die Version zur gerade laufenden passt und noch nicht im Cache liegt - ins lokale
/// <see cref="UpdatePackageCacheStore"/>, sodass <c>UpdatePackageDistributionService</c> es
/// sofort an andere Peers weiterverteilen kann. Rollenunabhängig (kein Admin-Sonderfall) -
/// welches Gerät zuerst eine neue Version bekommt, ist ein Betriebsvorgang, kein
/// App-internes Rollenkonzept.
///
/// Setzt NICHT <see cref="Models.SharedConfig.UpdateRollout"/> - die eigentliche Freigabe,
/// dass andere Geräte diese Version überhaupt pullen dürfen, bleibt ein bewusster
/// Admin-Klick im Dashboard (CLAUDE.md: "Rollout ist gestaffelt und wird vom Admin
/// freigegeben, nicht unkontrolliert lauffeuerartig").
/// </summary>
public static class UpdateSeedImporter
{
    private const string SeedFolderName = "update-seed";

    /// <summary>
    /// <paramref name="seedDirectoryOverride"/> und <paramref name="publicKeyOverride"/>
    /// sind nur für Tests gedacht (gleiches Muster wie die tcpPort-Overrides der
    /// Netzwerk-Services bzw. der 3-Parameter-Overload von <see cref="UpdatePackageVerifier"/>)
    /// - im echten Betrieb liegt der Ordner immer neben der laufenden exe und wird immer
    /// gegen den eingebetteten Produktionsschlüssel geprüft.
    /// </summary>
    public static bool TryImport(
        UpdatePackageCacheStore cacheStore,
        string currentProgramVersion,
        Action<string>? audit = null,
        string? seedDirectoryOverride = null,
        byte[]? publicKeyOverride = null)
    {
        try
        {
            var seedDir = seedDirectoryOverride ?? Path.Combine(AppContext.BaseDirectory, SeedFolderName);
            var manifestPath = Path.Combine(seedDir, "manifest.json");
            var payloadPath = Path.Combine(seedDir, "package.zip");
            if (!File.Exists(manifestPath) || !File.Exists(payloadPath))
            {
                return false; // kein Installer-Bausatz mitgeliefert (z. B. Test-Installer) - nichts zu tun, kein Fehler
            }

            UpdatePackageManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<UpdatePackageManifest>(File.ReadAllText(manifestPath));
            }
            catch (JsonException)
            {
                manifest = null;
            }

            if (manifest is null || manifest.Version != currentProgramVersion)
            {
                return false; // Bausatz gehört zu einer anderen Version als der gerade laufenden (z. B. nach einem späteren P2P-Swap) - veraltet, nicht mehr relevant
            }

            if (cacheStore.TryLoad(manifest.Version) is not null)
            {
                return false; // schon im Cache (z. B. weil dieses Gerät die Version stattdessen schon per P2P gezogen hatte)
            }

            var payload = File.ReadAllBytes(payloadPath);
            if (!UpdatePackageVerifier.Verify(payload, manifest, publicKeyOverride ?? UpdateSignaturePublicKey.Bytes))
            {
                audit?.Invoke($"update-seed: Signaturprüfung fehlgeschlagen für Version={manifest.Version} - NICHT importiert");
                return false;
            }

            cacheStore.Save(manifest.Version, manifest, payload);
            audit?.Invoke($"update-seed: Version={manifest.Version} aus dem Installer-Bausatz ins P2P-Cache importiert - dieses Gerät dient jetzt als Quelle für andere Peers");
            return true;
        }
        catch (Exception ex)
        {
            // Best-effort: ein kaputter/fehlender Bausatz darf den Agent-Start nie verhindern.
            audit?.Invoke($"update-seed: Import fehlgeschlagen: {ex.Message}");
            return false;
        }
    }
}
