using HaelpMi.Core.Licensing;
using HaelpMi.Core.Storage;
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
    public void Load_ReturnsInvalid_WhenExpiryDateIsManuallyExtended()
    {
        // Nutzerfrage 01.09.2026: "was hindert mich daran, das Ablaufdatum in der Datei
        // einfach zu ändern?" - ExpiryDateUtc ist Teil der signierten Felder
        // (License.GetSigningPayload), eine Änderung daran bricht die Signatur genauso wie
        // beim UserLimit-Fall oben. Eigener Test statt nur Analogieschluss, weil genau
        // dieses Feld die konkrete Sorge war.
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddDays(-1)), privateKey);

        var tampered = signed with { ExpiryDateUtc = DateTime.UtcNow.AddYears(10) };
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

    // Issue #54-Nacharbeit (01.09.2026): Lizenz als kompakter Text statt Datei-Anhang.
    [Fact]
    public void LoadFromKeyText_ReturnsValid_ForCorrectlySignedKeyText()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var keyText = LicenseKeyText.Encode(signed);

        var result = LicenseReader.LoadFromKeyText(keyText, customerGroupId, publicKeyBytes);

        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.Equal(LicenseTier.S, result.License!.Tier);
        Assert.Equal(25, result.License.UserLimit);
    }

    [Fact]
    public void LoadFromKeyText_ReturnsInvalid_WhenPayloadSegmentIsManuallyEdited()
    {
        // Gleicher Angriff wie Load_ReturnsInvalid_WhenExpiryDateIsManuallyExtended, nur am
        // Text statt an der JSON-Datei: der mittlere ("Payload") Teil wird verändert, die
        // Signatur (letzter Teil) bleibt wie erzeugt - genau das Szenario "Datum im
        // Lizenzschlüssel-Text von Hand ändern".
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddDays(-1)), privateKey);
        var keyText = LicenseKeyText.Encode(signed);

        var segments = keyText.Split('.');
        var payload = segments[1];
        segments[1] = payload[..^1] + (payload[^1] == 'A' ? 'B' : 'A');
        var tampered = string.Join('.', segments);

        var result = LicenseReader.LoadFromKeyText(tampered, customerGroupId, publicKeyBytes);

        Assert.Equal(LicenseStatus.Invalid, result.Status);
    }

    [Fact]
    public void LoadFromKeyText_ReturnsInvalid_ForGarbageText()
    {
        var (_, publicKeyBytes) = GenerateTestKeyPair();

        var result = LicenseReader.LoadFromKeyText("das ist kein Lizenzschlüssel", Guid.NewGuid(), publicKeyBytes);

        Assert.Equal(LicenseStatus.Invalid, result.Status);
    }

    // Regressionstest ("Lizenz einspielen"-Button reagierte gar nicht, Nutzerbericht
    // 02.09.2026, Issue #55): BouncyCastles Ed25519PublicKeyParameters-Konstruktor wirft
    // ArgumentException("invalid public key") für einen strukturell ungültigen Schlüssel
    // (32 Nullbytes sind kein gültiger Ed25519-Punkt - der Fall, wenn deployment.json einen
    // beschädigten LicensePublicKeyHex trägt). Ohne den Fang in TryVerifySignature riss das
    // bis zum globalen DispatcherUnhandledException-Handler der UI durch - der Button
    // wirkte dadurch wirkungslos, ohne jede Fehlermeldung, statt sauber "Invalid" zu liefern.
    [Fact]
    public void LoadFromKeyText_ReturnsInvalidRatherThanThrowing_WhenEmbeddedPublicKeyIsStructurallyInvalid()
    {
        var (privateKey, _) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var keyText = LicenseKeyText.Encode(signed);

        var malformedPublicKey = new byte[32]; // strukturell ungültig, wie ein beschädigtes deployment.json

        var exception = Record.Exception(() => LicenseReader.LoadFromKeyText(keyText, customerGroupId, malformedPublicKey));
        Assert.Null(exception);

        var result = LicenseReader.LoadFromKeyText(keyText, customerGroupId, malformedPublicKey);
        Assert.Equal(LicenseStatus.Invalid, result.Status);
    }

    [Fact]
    public void Load_ReturnsInvalidRatherThanThrowing_WhenEmbeddedPublicKeyIsStructurallyInvalid()
    {
        var (privateKey, _) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var license = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var path = WriteTempLicenseFile(license);

        var malformedPublicKey = new byte[32];

        var exception = Record.Exception(() => LicenseReader.Load(path, customerGroupId, malformedPublicKey));
        Assert.Null(exception);

        var result = LicenseReader.Load(path, customerGroupId, malformedPublicKey);
        Assert.Equal(LicenseStatus.Invalid, result.Status);
    }

    // Regressionstest ("Lizenz einspielen"-Button gab wieder keine Rückmeldung, obwohl #55
    // bereits behoben war): AppPaths.LicenseFilePath lag im Installationsverzeichnis
    // ({app}) - das bekommt anders als {commonappdata}\HaelpMi keine users-modify-
    // Berechtigung (siehe [Dirs] in HaelpMiCommon.iss.inc), ein Schreibversuch der
    // rechtelos laufenden App warf dort eine ungefangene UnauthorizedAccessException.
    // Lizenz-Import MUSS also im selben, per Installer beschreibbaren Wurzelordner landen
    // wie settings.json/devices.json/shared-config.json - dieser Test hält das fest, damit
    // eine künftige Änderung nicht wieder auf AppContext.BaseDirectory zurückfällt.
    [Fact]
    public void LicenseFilePath_LivesUnderTheWritableRootFolder_NotTheInstallDirectory()
    {
        using var scope = new TestAppDataScope();

        Assert.StartsWith(AppPaths.RootFolder, AppPaths.LicenseFilePath);
    }
}
