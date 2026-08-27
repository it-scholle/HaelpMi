using System;
using HaelpMi.InstallCreator.Licensing;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Xunit;

namespace HaelpMi.InstallCreator.Tests;

/// <summary>
/// Issue #18: eigenes Wegwerf-Schlüsselpaar pro Test, nie ein echter Lizenzschlüssel.
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

    private static LicenseFile SampleLicense(LicenseTier tier = LicenseTier.S) => new(
        CustomerGroupId: Guid.NewGuid(),
        Tier: tier,
        UserLimit: tier.UserLimit(),
        IssuedAtUtc: new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc),
        ExpiryDateUtc: new DateTime(2027, 8, 27, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void CreateSigned_ThenVerify_WithMatchingPublicKey_Succeeds()
    {
        var (privateKey, publicKey) = GenerateTestKeyPair();
        var signed = LicenseFileSigner.CreateSigned(SampleLicense(), privateKey);

        Assert.True(LicenseFileSigner.Verify(signed, publicKey));
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

    // Theory-Parameter bewusst als string statt LicenseTier: die Enum ist wie der Rest von
    // HaelpMi.InstallCreator internal, ein öffentlicher Testmethoden-Parameter darf sie aber
    // nicht direkt aufnehmen (CS0051).
    [Theory]
    [InlineData("Trial", 10)]
    [InlineData("S", 25)]
    [InlineData("M", 75)]
    [InlineData("L", 150)]
    public void UserLimit_MatchesAgreedStaffelung(string tierName, int expected) =>
        Assert.Equal(expected, Enum.Parse<LicenseTier>(tierName).UserLimit());

    [Fact]
    public void UserLimit_ForXL_IsUnlimited() =>
        Assert.Null(LicenseTier.XL.UserLimit());
}
