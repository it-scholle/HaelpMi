using System.Security.Cryptography;
using System.Text.Json;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Security;

/// <summary>
/// Lädt/erzeugt das lokale Geräte-Identitätsschlüsselpaar (siehe DeviceIdentitySigner).
/// Anders als settings.json/devices.json wird der private Schlüssel über DPAPI
/// geschützt abgelegt - LocalMachine-Scope, bewusst NICHT CurrentUser: HälpMi läuft laut
/// AutostartRegistrar ohne Bindung an ein bestimmtes Windows-Konto (mehrere Nutzer teilen
/// sich ein physisches Gerät, Bugfix 08.08.2026), ein CurrentUser-Scope würde bei jedem
/// Kontenwechsel eine "neue" Geräte-Identität erzwingen und damit das Trust-on-First-Use-
/// Pinning auf Peer-Seite sabotieren (das Gerät sähe für jeden Windows-Nutzer anders aus).
/// Das schützt vor Auslesen von einem anderen physischen Gerät, NICHT vor einem zweiten
/// lokalen Konto auf demselben Gerät - dieselbe Grenze, die AppPaths (ProgramData,
/// schreibbar für normale Nutzerkonten) für den Rest des lokalen Zustands schon zieht;
/// das Bedrohungsmodell dieses Projekts behandelt das Netzwerk als feindlich, nicht einen
/// zweiten lokalen Account auf demselben physischen Gerät.
/// </summary>
public static class DeviceIdentityStore
{
    private sealed class StoredIdentity
    {
        public required string PublicKeyBase64 { get; set; }
        public required string ProtectedPrivateKeyBase64 { get; set; }
    }

    private static DeviceIdentitySigner.KeyPair? _cached;

    /// <summary>
    /// Lädt das vorhandene Schlüsselpaar oder erzeugt beim allerersten Aufruf eines
    /// Geräts eines und persistiert es sofort. Prozessweit gecacht, da diese Methode auf
    /// jedem ausgehenden SecureEnvelope aufgerufen wird.
    /// </summary>
    public static DeviceIdentitySigner.KeyPair LoadOrCreate()
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        var path = AppPaths.DeviceIdentityFilePath;
        var keyPair = TryLoad(path) ?? CreateAndPersist(path);
        _cached = keyPair;
        return keyPair;
    }

    /// <summary>Test-only hook - erzwingt beim nächsten LoadOrCreate ein erneutes Lesen/Erzeugen statt des Prozess-Caches.</summary>
    internal static void ResetCacheForTests() => _cached = null;

    private static DeviceIdentitySigner.KeyPair? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredIdentity>(File.ReadAllText(path));
            if (stored is null)
            {
                return null;
            }

            var protectedBytes = Convert.FromBase64String(stored.ProtectedPrivateKeyBase64);
            var privateKeyBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
            return new DeviceIdentitySigner.KeyPair(stored.PublicKeyBase64, Convert.ToBase64String(privateKeyBytes));
        }
        catch (Exception)
        {
            // Beschädigte oder nicht mehr entschlüsselbare Datei (z. B. nach einer Kopie
            // auf ein anderes Gerät - DPAPI LocalMachine ist absichtlich nicht portabel)
            // darf den Programmstart nie verhindern - dann wird einfach ein neues
            // Schlüsselpaar erzeugt. Konsequenz: Peers sehen ab jetzt eine geänderte
            // Geräte-Identität für dieselbe DeviceId (siehe DiscoveryService-Pinning-
            // Logik) - bewusst in Kauf genommener "TOFU-Neustart"-Fall, kein Sicherheitsloch,
            // da eine echte DeviceId ohnehin nur bei einer Neuinstallation neu vergeben wird.
            return null;
        }
    }

    private static DeviceIdentitySigner.KeyPair CreateAndPersist(string path)
    {
        var keyPair = DeviceIdentitySigner.GenerateKeyPair();
        var privateKeyBytes = Convert.FromBase64String(keyPair.PrivateKeyBase64);
        var protectedBytes = ProtectedData.Protect(privateKeyBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);

        var stored = new StoredIdentity
        {
            PublicKeyBase64 = keyPair.PublicKeyBase64,
            ProtectedPrivateKeyBase64 = Convert.ToBase64String(protectedBytes),
        };

        AppPaths.EnsureRootExists();
        File.WriteAllText(path, JsonSerializer.Serialize(stored));
        return keyPair;
    }
}
