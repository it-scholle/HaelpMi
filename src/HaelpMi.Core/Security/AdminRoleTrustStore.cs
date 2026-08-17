using System.Security.Cryptography;
using System.Text.Json;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Security;

/// <summary>
/// Migrationspfad für die Admin-Rollen-Signatur (Nutzerwunsch 17.08.2026): eine
/// Neuinstallation bringt <see cref="Models.DeploymentInfo.AdminRolePublicKeyBase64"/>/
/// <c>AdminRolePrivateKeyBase64</c> bereits vom Install-Creator mit - aber
/// <c>deployment.json</c> wird laut Architektur nie vom laufenden Programm neu
/// geschrieben, ein reines Programm-Update kann also einem BESTANDS-Admin-Gerät diesen
/// Schlüssel nicht automatisch nachliefern. Dieser Store schließt genau diese Lücke, rein
/// lokal je Gerät, ohne dass irgendjemand manuell etwas installieren/ausführen muss:
///
/// - Ein Admin-Gerät ganz ohne Schlüssel (weder deployment.json noch dieser Store) erzeugt
///   sich beim ersten Start mit diesem Feature selbst ein Paar (<see cref="SaveOwnKey"/>,
///   Herkunft <see cref="PrivateKeyProvenance.SelfGenerated"/>).
/// - Treffen sich zwei Admin-Geräte mit UNTERSCHIEDLICHen selbst erzeugten Schlüsseln,
///   konvergiert die Gruppe deterministisch auf den lexikografisch kleineren öffentlichen
///   Schlüssel (siehe DiscoveryService) - das unterlegene Gerät übernimmt den Schlüssel
///   komplett (Herkunft <see cref="PrivateKeyProvenance.PeerAdopted"/>).
/// - Ein Gerät MIT Installer-Schlüssel (<c>DeploymentInfo.AdminRolePrivateKeyBase64</c>)
///   benutzt diesen Store für den eigenen privaten Schlüssel gar nicht - Installer-
///   Herkunft hat immer Vorrang und wird nie durch einen hier gespeicherten Wert ersetzt
///   (der Aufrufer, DiscoveryService, prüft DeploymentInfo IMMER zuerst).
/// - <see cref="PinnedGroupPublicKeyBase64"/> ist getrennt davon: der öffentliche
///   Schlüssel der eigenen Kunden-Gruppe, den JEDES Gerät (Admin wie User) braucht, um
///   fremde Admin-Behauptungen zu verifizieren (siehe AdminRoleVerifier) - Trust-on-
///   First-Use, genau wie <c>DeviceEntry.PinnedDeviceIdentityPublicKeyBase64</c>: einmal
///   gepinnt, wird ein abweichender später gemeldeter Schlüssel nicht mehr übernommen.
///
/// Restrisiko (bewusst in Kauf genommen, siehe CLAUDE.md-Migrationsnotiz): solange ein
/// Kreis noch gar keinen Admin-Rollen-Schlüssel besitzt, ist "wer zuerst kontaktiert,
/// gewinnt" theoretisch angreifbar - dieselbe TOFU-Grenze, die für die Geräte-Identität
/// bereits akzeptiert ist. Neuinstallationen bleiben über den Install-Creator-Weg frei
/// von diesem Zeitfenster.
/// </summary>
public static class AdminRoleTrustStore
{
    public enum PrivateKeyProvenance
    {
        SelfGenerated,
        PeerAdopted,
    }

    public sealed record TrustState(
        string? OwnPublicKeyBase64,
        string? OwnPrivateKeyBase64,
        PrivateKeyProvenance? OwnKeyProvenance,
        string? PinnedGroupPublicKeyBase64);

    private sealed class StoredTrust
    {
        public string? OwnPublicKeyBase64 { get; set; }
        public string? ProtectedOwnPrivateKeyBase64 { get; set; }
        public PrivateKeyProvenance? OwnKeyProvenance { get; set; }
        public string? PinnedGroupPublicKeyBase64 { get; set; }
    }

    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private static TrustState? _cached;

    public static TrustState Load()
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        var loaded = TryLoadFromDisk() ?? new TrustState(null, null, null, null);
        _cached = loaded;
        return loaded;
    }

    /// <summary>Test-only hook - erzwingt beim nächsten Load() ein erneutes Lesen statt des Prozess-Caches.</summary>
    internal static void ResetCacheForTests() => _cached = null;

    /// <summary>
    /// Speichert den eigenen effektiven privaten Schlüssel dieses Geräts (Selbst-Erzeugung
    /// oder Übernahme von einem Peer, siehe Klassendoku) - überschreibt einen zuvor hier
    /// gespeicherten Wert vorbehaltlos, die Konvergenz-/Vorrang-Entscheidung (Installer >
    /// PeerAdopted/SelfGenerated, kleinerer Schlüssel gewinnt bei einem Konflikt) trifft
    /// ausschließlich der Aufrufer (DiscoveryService), nicht dieser Store. Setzt
    /// <see cref="TrustState.PinnedGroupPublicKeyBase64"/> IMMER im selben Zug mit -
    /// solange dieses Gerät selbst ein Admin-Gerät mit eigenem Schlüssel ist, IST der
    /// eigene Schlüssel seine Vorstellung vom gültigen Gruppenschlüssel (bis eine
    /// Konvergenz das ändert, wieder über diese Methode).
    /// </summary>
    public static void SaveOwnKey(string publicKeyBase64, string privateKeyBase64, PrivateKeyProvenance provenance)
    {
        FileLock.Wait();
        try
        {
            var current = TryLoadFromDisk() ?? new TrustState(null, null, null, null);
            var updated = current with
            {
                OwnPublicKeyBase64 = publicKeyBase64,
                OwnPrivateKeyBase64 = privateKeyBase64,
                OwnKeyProvenance = provenance,
                PinnedGroupPublicKeyBase64 = publicKeyBase64,
            };
            PersistToDisk(updated);
            _cached = updated;
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <summary>
    /// Selbst-Erzeugung für den Migrationspfad (siehe Klassendoku): No-op, sobald IRGENDEIN
    /// eigener Schlüssel existiert - ein Installer-Schlüssel (<paramref
    /// name="installerPrivateKeyBase64"/> gesetzt) hat immer Vorrang und wird hier nie
    /// angefasst; existiert bereits ein hier gespeicherter Wert (frühere Selbst-Erzeugung
    /// oder Übernahme von einem Peer), wird ebenfalls nichts getan. Nur ein Admin-Gerät
    /// komplett ohne jeden Schlüssel erzeugt sich eines, Herkunft <see
    /// cref="PrivateKeyProvenance.SelfGenerated"/>.
    /// </summary>
    public static void EnsureSelfGeneratedKeyIfNeeded(Role role, string? installerPrivateKeyBase64)
    {
        if (role != Role.Admin || !string.IsNullOrEmpty(installerPrivateKeyBase64))
        {
            return;
        }

        FileLock.Wait();
        try
        {
            var current = TryLoadFromDisk() ?? new TrustState(null, null, null, null);
            if (!string.IsNullOrEmpty(current.OwnPrivateKeyBase64))
            {
                return;
            }

            var keyPair = AdminRoleSigner.GenerateKeyPair();
            var updated = current with
            {
                OwnPublicKeyBase64 = keyPair.PublicKeyBase64,
                OwnPrivateKeyBase64 = keyPair.PrivateKeyBase64,
                OwnKeyProvenance = PrivateKeyProvenance.SelfGenerated,
                // Gleicher Grund wie bei SaveOwnKey: der frisch erzeugte eigene Schlüssel
                // ist bis zu einer Konvergenz auch die eigene Vorstellung vom Gruppenschlüssel.
                PinnedGroupPublicKeyBase64 = keyPair.PublicKeyBase64,
            };
            PersistToDisk(updated);
            _cached = updated;
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <summary>
    /// Pinnt den öffentlichen Gruppenschlüssel beim allerersten Mal, dass dieses Gerät ihn
    /// (verifiziert, siehe AdminRoleVerifier) sieht - ein späterer abweichender Wert wird
    /// NICHT übernommen (Trust-on-First-Use, siehe Klassendoku). Kein Effekt, falls bereits
    /// gepinnt.
    /// </summary>
    public static void PinGroupPublicKeyIfUnset(string publicKeyBase64)
    {
        FileLock.Wait();
        try
        {
            var current = TryLoadFromDisk() ?? new TrustState(null, null, null, null);
            if (!string.IsNullOrEmpty(current.PinnedGroupPublicKeyBase64))
            {
                return;
            }

            var updated = current with { PinnedGroupPublicKeyBase64 = publicKeyBase64 };
            PersistToDisk(updated);
            _cached = updated;
        }
        finally
        {
            FileLock.Release();
        }
    }

    private static TrustState? TryLoadFromDisk()
    {
        var path = AppPaths.AdminRoleTrustFilePath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredTrust>(File.ReadAllText(path));
            if (stored is null)
            {
                return null;
            }

            string? privateKeyBase64 = null;
            if (!string.IsNullOrEmpty(stored.ProtectedOwnPrivateKeyBase64))
            {
                var protectedBytes = Convert.FromBase64String(stored.ProtectedOwnPrivateKeyBase64);
                var privateKeyBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
                privateKeyBase64 = Convert.ToBase64String(privateKeyBytes);
            }

            return new TrustState(stored.OwnPublicKeyBase64, privateKeyBase64, stored.OwnKeyProvenance, stored.PinnedGroupPublicKeyBase64);
        }
        catch (Exception)
        {
            // Beschädigte oder nicht mehr entschlüsselbare Datei darf den Programmstart nie
            // verhindern (gleiches Prinzip wie DeviceIdentityStore.TryLoad) - dann gilt
            // "kein lokaler Schlüssel/kein Pin", die Selbst-Erzeugung/das TOFU-Lernen
            // greifen erneut.
            return null;
        }
    }

    private static void PersistToDisk(TrustState state)
    {
        string? protectedPrivateKeyBase64 = null;
        if (!string.IsNullOrEmpty(state.OwnPrivateKeyBase64))
        {
            var privateKeyBytes = Convert.FromBase64String(state.OwnPrivateKeyBase64);
            var protectedBytes = ProtectedData.Protect(privateKeyBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
            protectedPrivateKeyBase64 = Convert.ToBase64String(protectedBytes);
        }

        var stored = new StoredTrust
        {
            OwnPublicKeyBase64 = state.OwnPublicKeyBase64,
            ProtectedOwnPrivateKeyBase64 = protectedPrivateKeyBase64,
            OwnKeyProvenance = state.OwnKeyProvenance,
            PinnedGroupPublicKeyBase64 = state.PinnedGroupPublicKeyBase64,
        };

        AppPaths.EnsureRootExists();
        File.WriteAllText(AppPaths.AdminRoleTrustFilePath, JsonSerializer.Serialize(stored));
    }
}
