using System.Security.Cryptography;
using HaelpMi.Core.Models;
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

    // --- UpdateOrchestrator.IsMyTurn: gestaffelte, kundengruppenweite Quote (Abschnitt 11,
    // seit 04.08.2026 global statt pro Kreis - siehe EditScope.cs für den Hintergrund) ---

    private static LiveIdentity MakeIdentity(Guid deviceId) =>
        new(Guid.NewGuid(), deviceId, "PC", "User", "Raum", "1", Role.User, false, "1.0.0", 0, DateTimeOffset.UtcNow);

    [Fact]
    public void IsMyTurn_False_WhenNoQuotaSet()
    {
        var identity = MakeIdentity(Guid.NewGuid());
        var config = new SharedConfig(); // ApprovedDeviceQuota bleibt 0

        Assert.False(UpdateOrchestrator.IsMyTurn(identity, config, new List<DeviceEntry>()));
    }

    [Fact]
    public void IsMyTurn_True_ForTheFirstNDevicesInStableDeviceIdOrder()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).OrderBy(id => id).ToList();
        var allDevices = ids.Select(id => new DeviceEntry { DeviceId = id }).ToList();
        var config = new SharedConfig { UpdateRollout = new UpdateRolloutState { ApprovedDeviceQuota = 2 } };

        // Wie im echten Betrieb: der Geräteliste-Provider liefert von JEDEM Gerät aus
        // gesehen "alle ANDEREN" - das eigene Gerät fügt IsMyTurn selbst hinzu.
        bool IsTurnFor(Guid deviceId) =>
            UpdateOrchestrator.IsMyTurn(MakeIdentity(deviceId), config, allDevices.Where(d => d.DeviceId != deviceId).ToList());

        // Die ersten beiden (Index 0,1) der stabil sortierten Geräte-IDs sind dran, die anderen beiden nicht.
        Assert.True(IsTurnFor(ids[0]));
        Assert.True(IsTurnFor(ids[1]));
        Assert.False(IsTurnFor(ids[2]));
        Assert.False(IsTurnFor(ids[3]));
    }
}
