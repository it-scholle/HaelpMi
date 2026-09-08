using HaelpMi.Core.Licensing;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Issue #94: automatischer Wechsel auf eine vorgemerkte Downgrade-Lizenz.</summary>
public class PendingLicenseSwitchTests
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

    private static License Sign(License unsigned, Ed25519PrivateKeyParameters privateKey)
    {
        var payload = unsigned.GetSigningPayload();
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        return unsigned with { SignatureBase64 = Convert.ToBase64String(signer.GenerateSignature()) };
    }

    private static string TempPath(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.json");

    [Fact]
    public void ApplyIfDue_Applies_WhenActiveLicenseExpiryIsReached()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var licenseFilePath = TempPath("lizenz");
        var pendingFilePath = TempPath("lizenz-pending");

        var active = Sign(new License(customerGroupId, LicenseTier.L, 25, DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddDays(-1), ""), privateKey);
        var pending = Sign(new License(customerGroupId, LicenseTier.S, 10, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddYears(1), ""), privateKey);
        File.WriteAllText(licenseFilePath, System.Text.Json.JsonSerializer.Serialize(active));
        File.WriteAllText(pendingFilePath, System.Text.Json.JsonSerializer.Serialize(pending));

        var applied = PendingLicenseSwitch.ApplyIfDue(licenseFilePath, pendingFilePath, customerGroupId, publicKeyBytes, DateTime.UtcNow);

        Assert.True(applied);
        Assert.False(File.Exists(pendingFilePath));
        var reCheck = LicenseReader.Load(licenseFilePath, customerGroupId, publicKeyBytes);
        Assert.Equal(LicenseTier.S, reCheck.License!.Tier);
    }

    [Fact]
    public void ApplyIfDue_DoesNothing_WhileActiveLicenseIsStillWithinItsOwnValidity()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var licenseFilePath = TempPath("lizenz");
        var pendingFilePath = TempPath("lizenz-pending");

        var active = Sign(new License(customerGroupId, LicenseTier.L, 25, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1), ""), privateKey);
        var pending = Sign(new License(customerGroupId, LicenseTier.S, 10, DateTime.UtcNow, DateTime.UtcNow.AddYears(2), ""), privateKey);
        File.WriteAllText(licenseFilePath, System.Text.Json.JsonSerializer.Serialize(active));
        File.WriteAllText(pendingFilePath, System.Text.Json.JsonSerializer.Serialize(pending));

        var applied = PendingLicenseSwitch.ApplyIfDue(licenseFilePath, pendingFilePath, customerGroupId, publicKeyBytes, DateTime.UtcNow);

        Assert.False(applied);
        Assert.True(File.Exists(pendingFilePath));
        var reCheck = LicenseReader.Load(licenseFilePath, customerGroupId, publicKeyBytes);
        Assert.Equal(LicenseTier.L, reCheck.License!.Tier);
    }

    [Fact]
    public void ApplyIfDue_AppliesImmediately_WhenActiveLicenseFileIsMissing()
    {
        // Nutzerentscheidung 08.09.2026: ohne gültig lesbare aktive Lizenz gibt es nichts,
        // das noch "regulär bis Datum genutzt" werden könnte.
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var licenseFilePath = TempPath("lizenz");
        var pendingFilePath = TempPath("lizenz-pending");

        var pending = Sign(new License(customerGroupId, LicenseTier.S, 10, DateTime.UtcNow, DateTime.UtcNow.AddYears(2), ""), privateKey);
        File.WriteAllText(pendingFilePath, System.Text.Json.JsonSerializer.Serialize(pending));

        var applied = PendingLicenseSwitch.ApplyIfDue(licenseFilePath, pendingFilePath, customerGroupId, publicKeyBytes, DateTime.UtcNow);

        Assert.True(applied);
        Assert.True(File.Exists(licenseFilePath));
        Assert.False(File.Exists(pendingFilePath));
    }

    [Fact]
    public void ApplyIfDue_ReturnsFalse_WhenNoPendingLicenseExists()
    {
        var (_, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var licenseFilePath = TempPath("lizenz");
        var pendingFilePath = TempPath("lizenz-pending");

        var applied = PendingLicenseSwitch.ApplyIfDue(licenseFilePath, pendingFilePath, customerGroupId, publicKeyBytes, DateTime.UtcNow);

        Assert.False(applied);
    }
}
