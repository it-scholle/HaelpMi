using System.Security.Cryptography;
using HaelpMi.Core.Updates;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Abschnitt 11: "nur signierte Programm-Updates werden von einem Client angenommen".
/// Nutzt ein eigenes Wegwerf-Schlüsselpaar (nie den echten eingebetteten Produktions-
/// schlüssel) über den 3-Parameter-Overload von <see cref="UpdatePackageVerifier.Verify(byte[], UpdatePackageManifest, byte[])"/>.
/// </summary>
public class UpdatesTests
{
    private static (Ed25519PrivateKeyParameters Private, byte[] PublicKeyBytes) GenerateTestKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();
        var priv = (Ed25519PrivateKeyParameters)keyPair.Private;
        var pub = (Ed25519PublicKeyParameters)keyPair.Public;
        return (priv, pub.GetEncoded());
    }

    private static UpdatePackageManifest SignPayload(byte[] payload, Ed25519PrivateKeyParameters privateKey, string version = "1.2.3")
    {
        var hash = SHA256.HashData(payload);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(hash, 0, hash.Length);
        var signature = signer.GenerateSignature();

        return new UpdatePackageManifest(version, Convert.ToHexString(hash).ToLowerInvariant(), Convert.ToBase64String(signature), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Verify_AcceptsPackage_SignedWithMatchingPrivateKey()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var payload = "fake-update-package-bytes"u8.ToArray();
        var manifest = SignPayload(payload, privateKey);

        Assert.True(UpdatePackageVerifier.Verify(payload, manifest, publicKeyBytes));
    }

    [Fact]
    public void Verify_RejectsPackage_WhenPayloadWasTamperedWithAfterSigning()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var originalPayload = "fake-update-package-bytes"u8.ToArray();
        var manifest = SignPayload(originalPayload, privateKey);

        var tamperedPayload = "fake-update-package-BYTES"u8.ToArray(); // ein Bit anders

        Assert.False(UpdatePackageVerifier.Verify(tamperedPayload, manifest, publicKeyBytes));
    }

    [Fact]
    public void Verify_RejectsPackage_SignedWithADifferentPrivateKey()
    {
        var (_, attackerPublicKeyBytes) = GenerateTestKeyPair();
        var (realPrivateKey, _) = GenerateTestKeyPair();
        var payload = "fake-update-package-bytes"u8.ToArray();

        // Mit dem "echten" Schlüssel signiert, aber gegen einen ANDEREN öffentlichen
        // Schlüssel geprüft (z. B. ein Angreifer, der sein eigenes Paar unterschiebt).
        var manifest = SignPayload(payload, realPrivateKey);

        Assert.False(UpdatePackageVerifier.Verify(payload, manifest, attackerPublicKeyBytes));
    }

    [Fact]
    public void Verify_RejectsPackage_WhenSignatureIsNotValidBase64()
    {
        var (_, publicKeyBytes) = GenerateTestKeyPair();
        var manifest = new UpdatePackageManifest("1.0.0", "deadbeef", "not-valid-base64!!!", DateTimeOffset.UtcNow);

        Assert.False(UpdatePackageVerifier.Verify("payload"u8.ToArray(), manifest, publicKeyBytes));
    }

    // --- UpdateOrchestrator.IsNewer: numerischer Versionsvergleich (Abschnitt 11) -------

    [Theory]
    [InlineData("1.2.0", "1.10.0", false)] // eine reine String-Ordnung würde das hier falsch herum einsortieren
    [InlineData("1.10.0", "1.2.0", true)]
    [InlineData("0.2.0", "0.1.9", true)]
    [InlineData("1.0.0", "1.0.0", false)] // gleiche Version ist nicht "neuer"
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("2.0.0", "1.9.9", true)]
    public void IsNewer_ComparesVersionsNumerically_NotAsPlainStrings(string candidate, string current, bool expectedNewer)
    {
        Assert.Equal(expectedNewer, UpdateOrchestrator.IsNewer(candidate, current));
    }

    // --- UpdateOrchestrator.IsMyTurn: entfernt am 13.08.2026 -------------------------------
    // Testete das frühere gestaffelte Freigabekontingent (ApprovedDeviceQuota, "die ersten N
    // Geräte in stabiler ID-Sortierung"). Nach der CLAUDE.md-Korrektur "Rollout-Freigabe"
    // (11.08.2026) entfällt die Staffelung ersatzlos: der Admin gibt eine Version genau
    // einmal frei, danach darf JEDES Gerät sie sofort ziehen (kein Kontingent-Gate mehr) -
    // IsMyTurn und ApprovedDeviceQuota existieren im Code nicht mehr, damit sind auch diese
    // Tests hinfällig.
}
