using System;
using HaelpMi.InstallCreator.Controls;
using HaelpMi.InstallCreator.Licensing;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Xunit;

namespace HaelpMi.InstallCreator.Tests;

/// <summary>
/// Issue #18: eigenes Wegwerf-Schlüsselpaar pro Test, nie ein echter Lizenzschlüssel.
/// <see cref="CreateSigned_VerifiesAgainstRealCoreLicenseReader"/> ist der eigentlich
/// wichtige Test hier - er hat beim ersten Rebase auf release-1.0-MVP einen echten
/// Formatunterschied zu HaelpMi.Core.Licensing.LicenseReader (Issue #19) aufgedeckt, den ein
/// reiner Roundtrip-Test gegen den eigenen Signierer nie gefunden hätte.
/// </summary>
public class LicenseFileSignerTests
{
    private static (byte[] PrivateKeyBytes, byte[] PublicKeyBytes) GenerateTestKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();
        var priv = (Ed25519PrivateKeyParameters)keyPair.Private;
        var pub = (Ed25519PublicKeyParameters)keyPair.Public;
        return (priv.GetEncoded(), pub.GetEncoded());
    }

    private static License SampleLicense(LicenseTier tier = LicenseTier.S) => new(
        CustomerGroupId: Guid.NewGuid(),
        Tier: tier,
        UserLimit: LicenseTierLimits.GetUserLimit(tier),
        IssuedAtUtc: new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc),
        ExpiryDateUtc: new DateTime(2027, 8, 27, 0, 0, 0, DateTimeKind.Utc),
        SignatureBase64: string.Empty);

    [Fact]
    public void CreateSigned_ThenVerify_WithMatchingPublicKey_Succeeds()
    {
        var (privateKey, publicKey) = GenerateTestKeyPair();
        var signed = LicenseFileSigner.CreateSigned(SampleLicense(), privateKey);

        Assert.True(LicenseFileSigner.Verify(signed, publicKey));
    }

    [Fact]
    public void CreateSigned_VerifiesAgainstRealCoreLicenseReader()
    {
        var (privateKey, publicKey) = GenerateTestKeyPair();
        var license = SampleLicense();
        var signed = LicenseFileSigner.CreateSigned(license, privateKey);

        var coreLicense = new HaelpMi.Core.Licensing.License(
            signed.CustomerGroupId,
            (HaelpMi.Core.Licensing.LicenseTier)(int)signed.Tier,
            signed.UserLimit,
            signed.IssuedAtUtc,
            signed.ExpiryDateUtc,
            signed.SignatureBase64);

        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tempFile, System.Text.Json.JsonSerializer.Serialize(coreLicense));
            var result = HaelpMi.Core.Licensing.LicenseReader.Load(tempFile, signed.CustomerGroupId, publicKey);
            Assert.Equal(HaelpMi.Core.Licensing.LicenseStatus.Valid, result.Status);
        }
        finally
        {
            System.IO.File.Delete(tempFile);
        }
    }

    // Issue #54-Nacharbeit (01.09.2026): Lizenz als kompakter Text statt Datei - derselbe
    // Grund wie bei CreateSigned_VerifiesAgainstRealCoreLicenseReader (unabhängig gebaute
    // Encode-/Decode-Hälften müssen tatsächlich zusammenpassen, nicht nur laut Doku-Kommentar).
    [Fact]
    public void LicenseKeyText_Encode_VerifiesAgainstRealCoreLicenseReader()
    {
        var (privateKey, publicKey) = GenerateTestKeyPair();
        var signed = LicenseFileSigner.CreateSigned(SampleLicense(), privateKey);

        var keyText = LicenseKeyText.Encode(signed);

        var result = HaelpMi.Core.Licensing.LicenseReader.LoadFromKeyText(keyText, signed.CustomerGroupId, publicKey);
        Assert.Equal(HaelpMi.Core.Licensing.LicenseStatus.Valid, result.Status);
    }

    [Fact]
    public void Verify_WithWrongPublicKey_Fails()
    {
        var (privateKey, _) = GenerateTestKeyPair();
        var (_, otherPublicKey) = GenerateTestKeyPair();
        var signed = LicenseFileSigner.CreateSigned(SampleLicense(), privateKey);

        Assert.False(LicenseFileSigner.Verify(signed, otherPublicKey));
    }

    [Fact]
    public void Verify_AfterTamperingWithUserLimit_Fails()
    {
        var (privateKey, publicKey) = GenerateTestKeyPair();
        var signed = LicenseFileSigner.CreateSigned(SampleLicense(), privateKey);

        var tampered = signed with { UserLimit = 999999 };

        Assert.False(LicenseFileSigner.Verify(tampered, publicKey));
    }

    // Issue #56: eigenes Schlüsselpaar pro Kundengruppe, hier erzeugt statt manuell geladen.
    [Fact]
    public void GenerateKeyPair_ProducesAWorkingPair_SignedWithPrivate_VerifiesWithPublic()
    {
        var (privateKeyBytes, publicKeyHex) = LicenseFileSigner.GenerateKeyPair();
        var publicKeyBytes = Convert.FromHexString(publicKeyHex);
        var signed = LicenseFileSigner.CreateSigned(SampleLicense(), privateKeyBytes);

        Assert.True(LicenseFileSigner.Verify(signed, publicKeyBytes));
    }

    [Fact]
    public void GenerateKeyPair_ProducesADifferentPairOnEachCall()
    {
        var (_, publicKeyHexA) = LicenseFileSigner.GenerateKeyPair();
        var (_, publicKeyHexB) = LicenseFileSigner.GenerateKeyPair();

        Assert.NotEqual(publicKeyHexA, publicKeyHexB);
    }

    [Fact]
    public void Verify_WithMalformedSignature_FailsInsteadOfThrowing()
    {
        var (_, publicKey) = GenerateTestKeyPair();
        var signed = LicenseFileSigner.CreateSigned(SampleLicense(), GenerateTestKeyPair().PrivateKeyBytes) with
        {
            SignatureBase64 = "nicht-base64!!",
        };

        Assert.False(LicenseFileSigner.Verify(signed, publicKey));
    }
}
