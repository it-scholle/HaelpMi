using System.Security.Cryptography;
using System.Text.Json;
using HaelpMi.Core.Storage;
using HaelpMi.Core.Updates;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Nutzerwunsch 09.08.2026: "vollautomatisch, sobald der Admin sich selbst aktualisiert
/// hat" - der Installer bringt dafür ein signiertes update-seed\ mit, das
/// <see cref="UpdateSeedImporter"/> beim ersten Start ins lokale P2P-Cache übernimmt.
/// Nutzt (wie <c>UpdatesTests</c>) ein eigenes Wegwerf-Schlüsselpaar über den
/// publicKeyOverride-Parameter statt des echten eingebetteten Produktionsschlüssels, den
/// eine Test-Umgebung nie besitzt (nur der öffentliche Teil ist im Repo, siehe
/// UpdateSignaturePublicKey.cs).
/// </summary>
public class UpdateSeedImporterTests
{
    // Nur für den update-seed-Ordner selbst - das eigentliche P2P-Cache läuft über
    // TestAppDataScope (AppPaths-Umleitung), dieser Ordner liegt außerhalb davon.
    private static string CreateSeedDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "haelpmi-seed-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (string SeedDir, UpdatePackageManifest Manifest, byte[] Payload, byte[] PublicKeyBytes) WriteValidSeed(string version, byte[]? payloadOverride = null)
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        var publicKeyBytes = ((Ed25519PublicKeyParameters)keyPair.Public).GetEncoded();

        var payload = payloadOverride ?? "fake-package-bytes"u8.ToArray();
        var hash = SHA256.HashData(payload);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(hash, 0, hash.Length);
        var signature = signer.GenerateSignature();

        var manifest = new UpdatePackageManifest(version, Convert.ToHexString(hash).ToLowerInvariant(), Convert.ToBase64String(signature), DateTimeOffset.UtcNow);

        var seedDir = CreateSeedDir();
        File.WriteAllText(Path.Combine(seedDir, "manifest.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllBytes(Path.Combine(seedDir, "package.zip"), payload);
        return (seedDir, manifest, payload, publicKeyBytes);
    }

    [Fact]
    public void TryImport_ImportsIntoCache_WhenVersionMatchesAndSignatureValid()
    {
        using var scope = new TestAppDataScope();
        var cacheStore = new UpdatePackageCacheStore();
        var (seedDir, manifest, payload, publicKeyBytes) = WriteValidSeed("2.0.0");

        var imported = UpdateSeedImporter.TryImport(cacheStore, manifest.Version, seedDirectoryOverride: seedDir, publicKeyOverride: publicKeyBytes);

        Assert.True(imported);
        var cached = cacheStore.TryLoad(manifest.Version);
        Assert.NotNull(cached);
        Assert.Equal(manifest.SignatureBase64, cached!.Value.Manifest.SignatureBase64);
        Assert.Equal(payload, cached.Value.Payload);
    }

    [Fact]
    public void TryImport_ReturnsFalse_WhenSignedWithADifferentKey()
    {
        // Genau der Fall, den die Prüfung eigentlich verhindern soll: ein Paket, das nicht
        // mit dem für DIESE Prüfung erwarteten Schlüssel signiert wurde (hier simuliert
        // durch zwei unabhängige Test-Schlüsselpaare statt eines echten Angreifers).
        using var scope = new TestAppDataScope();
        var cacheStore = new UpdatePackageCacheStore();
        var (seedDir, manifest, _, _) = WriteValidSeed("2.0.0");
        var (_, _, _, attackerPublicKeyBytes) = WriteValidSeed("2.0.0"); // unabhängiges zweites Schlüsselpaar, nur der öffentliche Teil wird gebraucht

        var imported = UpdateSeedImporter.TryImport(cacheStore, manifest.Version, seedDirectoryOverride: seedDir, publicKeyOverride: attackerPublicKeyBytes);

        Assert.False(imported);
        Assert.Null(cacheStore.TryLoad(manifest.Version));
    }

    [Fact]
    public void TryImport_ReturnsFalse_WhenSeedFolderMissing()
    {
        using var scope = new TestAppDataScope();
        var cacheStore = new UpdatePackageCacheStore();

        var imported = UpdateSeedImporter.TryImport(cacheStore, "1.0.0", seedDirectoryOverride: Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()));

        Assert.False(imported);
    }

    [Fact]
    public void TryImport_ReturnsFalse_WhenSeedVersionDoesNotMatchRunningVersion()
    {
        using var scope = new TestAppDataScope();
        var cacheStore = new UpdatePackageCacheStore();
        var (seedDir, manifest, _, publicKeyBytes) = WriteValidSeed("2.0.0");

        var imported = UpdateSeedImporter.TryImport(cacheStore, "1.9.9", seedDirectoryOverride: seedDir, publicKeyOverride: publicKeyBytes);

        Assert.False(imported);
        Assert.Null(cacheStore.TryLoad(manifest.Version));
    }

    [Fact]
    public void TryImport_ReturnsFalse_WhenAlreadyCached()
    {
        using var scope = new TestAppDataScope();
        var cacheStore = new UpdatePackageCacheStore();
        var (seedDir, manifest, payload, publicKeyBytes) = WriteValidSeed("2.0.0");

        // Schon per P2P-Pull im Cache (unabhängig davon, ob dessen Signatur gültig wäre) -
        // TryImport darf einen bereits vorhandenen Cache-Eintrag nie überschreiben.
        cacheStore.Save(manifest.Version, manifest, payload);

        var imported = UpdateSeedImporter.TryImport(cacheStore, manifest.Version, seedDirectoryOverride: seedDir, publicKeyOverride: publicKeyBytes);

        Assert.False(imported);
    }

    [Fact]
    public void TryImport_NeverThrows_OnCorruptManifest()
    {
        using var scope = new TestAppDataScope();
        var cacheStore = new UpdatePackageCacheStore();
        var seedDir = CreateSeedDir();
        File.WriteAllText(Path.Combine(seedDir, "manifest.json"), "{ kein gueltiges json");
        File.WriteAllBytes(Path.Combine(seedDir, "package.zip"), "irrelevant"u8.ToArray());

        var imported = UpdateSeedImporter.TryImport(cacheStore, "1.0.0", seedDirectoryOverride: seedDir);

        Assert.False(imported);
    }
}
