using HaelpMi.Core.Licensing;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Issue #20 (Warnstufen-Banner) und Issue #51 (Lizenz-Import), beide auf #19 aufgebaut.</summary>
public class LicenseWarningAndImportTests
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

    private static License SignLicense(License unsigned, Ed25519PrivateKeyParameters privateKey)
    {
        var payload = unsigned.GetSigningPayload();
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        var signature = signer.GenerateSignature();
        return unsigned with { SignatureBase64 = Convert.ToBase64String(signature) };
    }

    private static License MakeUnsigned(Guid customerGroupId, DateTime expiryUtc) =>
        new(customerGroupId, LicenseTier.S, 25, DateTime.UtcNow.AddDays(-1), expiryUtc, SignatureBase64: string.Empty);

    // --------------------------------------------------------- LicenseWarningEvaluator ---

    [Fact]
    public void Evaluate_ReturnsNone_ForValidLicense_FarFromExpiry()
    {
        var result = new LicenseCheckResult(LicenseStatus.Valid, MakeUnsigned(Guid.NewGuid(), DateTime.UtcNow.AddYears(1)) with { SignatureBase64 = "x" });

        var warning = LicenseWarningEvaluator.Evaluate(result, DateTime.UtcNow);

        Assert.Equal(LicenseWarningLevel.None, warning.Level);
    }

    [Fact]
    public void Evaluate_ReturnsExpiringSoon_ForValidLicense_WithinWarningWindow()
    {
        var expiry = DateTime.UtcNow.AddDays(LicenseWarningEvaluator.WarningWindowDays - 1);
        var result = new LicenseCheckResult(LicenseStatus.Valid, MakeUnsigned(Guid.NewGuid(), expiry) with { SignatureBase64 = "x" });

        var warning = LicenseWarningEvaluator.Evaluate(result, DateTime.UtcNow);

        Assert.Equal(LicenseWarningLevel.ExpiringSoon, warning.Level);
        Assert.True(warning.DaysRemaining is >= 0 and < LicenseWarningEvaluator.WarningWindowDays);
    }

    [Theory]
    [InlineData(LicenseStatus.Missing, LicenseWarningLevel.Missing)]
    [InlineData(LicenseStatus.Invalid, LicenseWarningLevel.Invalid)]
    public void Evaluate_MapsMissingAndInvalid_Directly(LicenseStatus status, LicenseWarningLevel expectedLevel)
    {
        var warning = LicenseWarningEvaluator.Evaluate(new LicenseCheckResult(status, null), DateTime.UtcNow);

        Assert.Equal(expectedLevel, warning.Level);
        Assert.Null(warning.DaysRemaining);
    }

    [Fact]
    public void Evaluate_ReturnsExpired_ForPastExpiryDate()
    {
        var expiry = DateTime.UtcNow.AddDays(-5);
        var result = new LicenseCheckResult(LicenseStatus.Expired, MakeUnsigned(Guid.NewGuid(), expiry) with { SignatureBase64 = "x" });

        var warning = LicenseWarningEvaluator.Evaluate(result, DateTime.UtcNow);

        Assert.Equal(LicenseWarningLevel.Expired, warning.Level);
        Assert.True(warning.DaysRemaining < 0);
    }

    // --------------------------------------------------------------------- LicenseImporter ---

    [Fact]
    public void Import_CopiesFile_AndReturnsSuccess_ForValidLicense()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var license = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lizenz-import-src-{Guid.NewGuid():N}.json");
        File.WriteAllText(sourcePath, System.Text.Json.JsonSerializer.Serialize(license));
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-import-dst-{Guid.NewGuid():N}.json");

        var result = LicenseImporter.Import(sourcePath, customerGroupId, destinationPath, publicKeyBytes);

        Assert.True(result.Success);
        Assert.Equal(LicenseStatus.Valid, result.CheckResult.Status);
        Assert.True(File.Exists(destinationPath));

        // Die kopierte Datei muss über denselben Reader erneut gültig sein (Rundreise).
        var reCheck = LicenseReader.Load(destinationPath, customerGroupId, publicKeyBytes);
        Assert.Equal(LicenseStatus.Valid, reCheck.Status);
    }

    [Fact]
    public void Import_DoesNotOverwriteDestination_ForTamperedLicense()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var tampered = signed with { UserLimit = 999999 };
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lizenz-import-src-{Guid.NewGuid():N}.json");
        File.WriteAllText(sourcePath, System.Text.Json.JsonSerializer.Serialize(tampered));
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-import-dst-{Guid.NewGuid():N}.json");
        File.WriteAllText(destinationPath, "bestehende-alte-lizenz");

        var result = LicenseImporter.Import(sourcePath, customerGroupId, destinationPath, publicKeyBytes);

        Assert.False(result.Success);
        Assert.Equal(LicenseStatus.Invalid, result.CheckResult.Status);
        Assert.Equal("bestehende-alte-lizenz", File.ReadAllText(destinationPath));
    }

    [Fact]
    public void Import_Succeeds_ForAlreadyExpiredButCorrectlySignedLicense()
    {
        // Soft-Expiry (CLAUDE.md): eine echte, nur schon abgelaufene Lizenz darf trotzdem
        // eingespielt werden (z. B. Testszenario, oder eine verspätet zugestellte Erneuerung).
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var license = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddDays(-1)), privateKey);
        var sourcePath = Path.Combine(Path.GetTempPath(), $"lizenz-import-src-{Guid.NewGuid():N}.json");
        File.WriteAllText(sourcePath, System.Text.Json.JsonSerializer.Serialize(license));
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-import-dst-{Guid.NewGuid():N}.json");

        var result = LicenseImporter.Import(sourcePath, customerGroupId, destinationPath, publicKeyBytes);

        Assert.True(result.Success);
        Assert.Equal(LicenseStatus.Expired, result.CheckResult.Status);
    }
}
