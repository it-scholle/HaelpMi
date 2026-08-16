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
/// dass andere Geräte diese Version überhaupt pullen dürfen, bleibt ein bewusster,
/// einmaliger Admin-Klick im Dashboard (CLAUDE.md: "der Admin gibt das Update genau
/// einmal frei"); danach verbreitet sich die Version automatisch weiter.
/// </summary>
public static class UpdateSeedImporter
{
    private const string SeedFolderName = "update-seed";

    /// <summary>
    /// <paramref name="seedDirectoryOverride"/> ist nur für Tests gedacht (gleiches Muster
    /// wie die tcpPort-Overrides der Netzwerk-Services) - im echten Betrieb liegt der Ordner
    /// immer neben der laufenden exe. <paramref name="publicKeyOverride"/> wird seit
    /// 16.08.2026 auch produktiv genutzt (App.xaml.cs übergibt hier
    /// <see cref="Models.DeploymentInfo.UpdatePublicKeyBase64"/> dieser Installation, sofern
    /// vorhanden) - ohne Angabe (Tests mit einem Wegwerf-Schlüsselpaar, oder alte
    /// Installer-Stände ohne das Feld) gilt weiterhin der eingebettete Produktionsschlüssel.
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
