using HaelpMi.Core.Licensing;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Deckt die codeseitig automatisierbaren Fälle aus dem manuellen Testplan-Hinweis in
/// Issue #19 ab (gültig/Ablauf/manipuliert/fehlend) - der dortige Punkt 2 (echtes
/// Systemdatum verstellen) bleibt laut Issue-Kommentar bewusst ein manueller Test.
/// Nutzt wie <c>UpdatesTests</c> ein eigenes Wegwerf-Schlüsselpaar über den internen
/// 3-Parameter-Overload von <see cref="LicenseReader.Load(string, Guid, byte[])"/>.
/// </summary>
public class LicensingTests
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

    private static License MakeUnsigned(Guid customerGroupId, DateTime expiryUtc, LicenseTier tier = LicenseTier.S, int? userLimit = 25) =>
        new(customerGroupId, tier, userLimit, DateTime.UtcNow.AddDays(-1), expiryUtc, SignatureBase64: string.Empty);

    private static string WriteTempLicenseFile(License license)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lizenz-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(license));
        return path;
    }

    [Fact]
    public void Load_ReturnsValid_ForCorrectlySignedLicense_MatchingCustomerGroup_NotYetExpired()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var license = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var path = WriteTempLicenseFile(license);

        var result = LicenseReader.Load(path, customerGroupId, publicKeyBytes);

        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.NotNull(result.License);
        Assert.Equal(LicenseTier.S, result.License!.Tier);
        Assert.Equal(25, result.License.UserLimit);
    }

    [Fact]
    public void Load_ReturnsExpired_NotInvalid_ForCorrectlySignedLicense_PastExpiryDate()
    {
        // Soft-Expiry (CLAUDE.md "Lizenz & Secrets": kein Hard-Lock) - eine abgelaufene,
        // aber sonst gültige Lizenz ist NICHT dasselbe Ergebnis wie eine manipulierte.
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var license = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddDays(-1)), privateKey);
        var path = WriteTempLicenseFile(license);

        var result = LicenseReader.Load(path, customerGroupId, publicKeyBytes);

        Assert.Equal(LicenseStatus.Expired, result.Status);
        Assert.NotNull(result.License);
    }

    [Fact]
    public void Load_ReturnsInvalid_WhenSignatureDoesNotMatchTamperedFields()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), privateKey);

        // Signatur bleibt wie erzeugt, aber ein Feld wird danach verändert - genau der
        // Manipulations-Fall aus dem Testplan-Hinweis in Issue #19.
        var tampered = signed with { UserLimit = 999999 };
        var path = WriteTempLicenseFile(tampered);

        var result = LicenseReader.Load(path, customerGroupId, publicKeyBytes);

        Assert.Equal(LicenseStatus.Invalid, result.Status);
        Assert.Null(result.License);
    }

    [Fact]
    public void Load_ReturnsInvalid_WhenSignedWithADifferentPrivateKey()
    {
        var (_, ownPublicKeyBytes) = GenerateTestKeyPair();
        var (attackerPrivateKey, _) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var license = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), attackerPrivateKey);
        var path = WriteTempLicenseFile(license);

        var result = LicenseReader.Load(path, customerGroupId, ownPublicKeyBytes);

        Assert.Equal(LicenseStatus.Invalid, result.Status);
    }

    [Fact]
    public void Load_ReturnsInvalid_ForValidlySignedLicense_BoundToADifferentCustomerGroup()
    {
        // #18-Diskussion zu #31: eine gültig signierte Lizenz eines ANDEREN Kunden darf
        // sich nicht einfach hier einspielen lassen.
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var licenseeGroupId = Guid.NewGuid();
        var ownGroupId = Guid.NewGuid();
        var license = SignLicense(MakeUnsigned(licenseeGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var path = WriteTempLicenseFile(license);

        var result = LicenseReader.Load(path, ownGroupId, publicKeyBytes);

        Assert.Equal(LicenseStatus.Invalid, result.Status);
    }

    [Fact]
    public void Load_ReturnsInvalid_ForMalformedJson()
    {
        var (_, publicKeyBytes) = GenerateTestKeyPair();
        var path = Path.Combine(Path.GetTempPath(), $"lizenz-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ das ist kein gueltiges JSON");

        var result = LicenseReader.Load(path, Guid.NewGuid(), publicKeyBytes);

        Assert.Equal(LicenseStatus.Invalid, result.Status);
    }

    [Fact]
    public void Load_ReturnsMissing_WhenNoFileExists()
    {
        var (_, publicKeyBytes) = GenerateTestKeyPair();
        var path = Path.Combine(Path.GetTempPath(), $"lizenz-test-{Guid.NewGuid():N}-nichtvorhanden.json");

        var result = LicenseReader.Load(path, Guid.NewGuid(), publicKeyBytes);

        Assert.Equal(LicenseStatus.Missing, result.Status);
        Assert.Null(result.License);
    }
}
