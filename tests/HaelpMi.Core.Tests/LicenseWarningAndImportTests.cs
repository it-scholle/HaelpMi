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

    // --------------------------------------- LicenseImporter.ImportFromKeyText (Fehlerbericht 02.09.2026) ---
    // Anders als der Datei-Import oben unterscheidet dieser Pfad die Ablehnungsgründe, damit
    // der "Lizenz einspielen"-Dialog eine konkrete statt einer generischen Meldung zeigen kann.

    [Fact]
    public void ImportFromKeyText_Activates_ForValidKey_MatchingCustomerGroup_NotExpired()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var keyText = LicenseKeyText.Encode(signed);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-keytext-dst-{Guid.NewGuid():N}.json");

        var diagnosis = LicenseImporter.ImportFromKeyText(keyText, customerGroupId, destinationPath, publicKeyBytes);

        Assert.Equal(LicenseImportOutcome.Activated, diagnosis.Outcome);
        Assert.NotNull(diagnosis.License);
        Assert.True(File.Exists(destinationPath));
    }

    [Fact]
    public void ImportFromKeyText_ReturnsExpired_WithoutPersisting_ForKeyPastItsExpiryDate()
    {
        // Abweichung vom Datei-Import (der Expired als Erfolg persistiert, Soft-Expiry für
        // eine bereits installierte Lizenz) - ein frisch importierter, schon abgelaufener
        // Schlüssel wird NICHT übernommen, siehe Klassenkommentar an ImportFromKeyText.
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddDays(-1)), privateKey);
        var keyText = LicenseKeyText.Encode(signed);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-keytext-dst-{Guid.NewGuid():N}.json");

        var diagnosis = LicenseImporter.ImportFromKeyText(keyText, customerGroupId, destinationPath, publicKeyBytes);

        Assert.Equal(LicenseImportOutcome.Expired, diagnosis.Outcome);
        Assert.NotNull(diagnosis.License);
        Assert.False(File.Exists(destinationPath));
    }

    [Fact]
    public void ImportFromKeyText_ReturnsWrongCustomer_WithoutPersisting_ForKeyBoundToADifferentCustomerGroup()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var licenseeGroupId = Guid.NewGuid();
        var ownGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(licenseeGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var keyText = LicenseKeyText.Encode(signed);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-keytext-dst-{Guid.NewGuid():N}.json");

        var diagnosis = LicenseImporter.ImportFromKeyText(keyText, ownGroupId, destinationPath, publicKeyBytes);

        Assert.Equal(LicenseImportOutcome.WrongCustomer, diagnosis.Outcome);
        Assert.NotNull(diagnosis.License); // authentisch lesbar, nur die falsche Kundengruppe
        Assert.False(File.Exists(destinationPath));
    }

    [Fact]
    public void ImportFromKeyText_ReturnsNotRecognized_WithoutPersisting_ForGarbageText()
    {
        var (_, publicKeyBytes) = GenerateTestKeyPair();
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-keytext-dst-{Guid.NewGuid():N}.json");

        var diagnosis = LicenseImporter.ImportFromKeyText("das ist kein Lizenzschlüssel", Guid.NewGuid(), destinationPath, publicKeyBytes);

        Assert.Equal(LicenseImportOutcome.NotRecognized, diagnosis.Outcome);
        Assert.Null(diagnosis.License);
        Assert.False(File.Exists(destinationPath));
    }

    [Fact]
    public void ImportFromKeyText_ReturnsNotRecognized_RatherThanThrowing_ForStructurallyInvalidEmbeddedPublicKey()
    {
        // Regressionstest für den eigentlichen Fehlerbericht (#55): mit dem damals noch nicht
        // ersetzten globalen Platzhalter-Prüfschlüssel (32 Nullbytes) warf der Ed25519-
        // Konstruktor ungefangen - der Button wirkte dadurch komplett wirkungslos statt
        // "nicht erkannt" zu melden. Ein strukturell ungültiger Schlüssel kann auch mit dem
        // Pro-Kundengruppe-Modell (#56) weiterhin vorkommen (beschädigtes deployment.json).
        var (privateKey, _) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var keyText = LicenseKeyText.Encode(signed);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-keytext-dst-{Guid.NewGuid():N}.json");
        var malformedPublicKey = new byte[32];

        var exception = Record.Exception(() => LicenseImporter.ImportFromKeyText(keyText, customerGroupId, destinationPath, malformedPublicKey));
        Assert.Null(exception);

        var diagnosis = LicenseImporter.ImportFromKeyText(keyText, customerGroupId, destinationPath, malformedPublicKey);
        Assert.Equal(LicenseImportOutcome.NotRecognized, diagnosis.Outcome);
    }

    // --------------------------------------------------- LicenseImporter.TryAdoptFromPeer ---
    // Issue #59/#60-Nachtrag "Lizenz sofort verteilen" (Nutzerbericht 07.09.2026).

    [Fact]
    public void TryAdoptFromPeer_Adopts_WhenNoLicenseHeldYet()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)), privateKey);
        var keyText = LicenseKeyText.Encode(signed);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-peer-dst-{Guid.NewGuid():N}.json");

        var adopted = LicenseImporter.TryAdoptFromPeer(keyText, customerGroupId, destinationPath, publicKeyBytes, currentLicense: null);

        Assert.True(adopted);
        Assert.True(File.Exists(destinationPath));
    }

    [Fact]
    public void TryAdoptFromPeer_Adopts_ExpiredLicense_UnlikeManualImportFromKeyText()
    {
        // Anders als ImportFromKeyText (Nutzervorgabe: manueller Import lehnt einen frisch
        // eingefügten, schon toten Schlüssel ab) - Soft-Expiry gilt für die automatische
        // P2P-Übernahme wie beim Datei-Import, siehe Klassenkommentar an TryAdoptFromPeer.
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var signed = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddDays(-1)), privateKey);
        var keyText = LicenseKeyText.Encode(signed);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-peer-dst-{Guid.NewGuid():N}.json");

        var adopted = LicenseImporter.TryAdoptFromPeer(keyText, customerGroupId, destinationPath, publicKeyBytes, currentLicense: null);

        Assert.True(adopted);
    }

    [Fact]
    public void TryAdoptFromPeer_Adopts_WhenPeerLicenseIsNewerThanOwnCurrentOne()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var older = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)) with { IssuedAtUtc = DateTime.UtcNow.AddDays(-10) }, privateKey);
        var newer = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)) with { IssuedAtUtc = DateTime.UtcNow }, privateKey);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-peer-dst-{Guid.NewGuid():N}.json");

        var adopted = LicenseImporter.TryAdoptFromPeer(LicenseKeyText.Encode(newer), customerGroupId, destinationPath, publicKeyBytes, currentLicense: older);

        Assert.True(adopted);
    }

    [Fact]
    public void TryAdoptFromPeer_Rejects_WhenPeerLicenseIsNotNewerThanOwnCurrentOne()
    {
        // Schützt vor Downgrade-Flapping durch eine ältere, aus dem Cache eines langsameren
        // Peers stammende Kopie (siehe Klassenkommentar).
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var current = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)) with { IssuedAtUtc = DateTime.UtcNow }, privateKey);
        var older = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)) with { IssuedAtUtc = DateTime.UtcNow.AddDays(-10) }, privateKey);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-peer-dst-{Guid.NewGuid():N}.json");

        var adopted = LicenseImporter.TryAdoptFromPeer(LicenseKeyText.Encode(older), customerGroupId, destinationPath, publicKeyBytes, currentLicense: current);

        Assert.False(adopted);
        Assert.False(File.Exists(destinationPath));
    }

    [Fact]
    public void TryAdoptFromPeer_Rejects_ForWrongCustomerGroup()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var signed = SignLicense(MakeUnsigned(Guid.NewGuid(), DateTime.UtcNow.AddYears(1)), privateKey);
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-peer-dst-{Guid.NewGuid():N}.json");

        var adopted = LicenseImporter.TryAdoptFromPeer(LicenseKeyText.Encode(signed), Guid.NewGuid(), destinationPath, publicKeyBytes, currentLicense: null);

        Assert.False(adopted);
        Assert.False(File.Exists(destinationPath));
    }

    [Fact]
    public void TryAdoptFromPeer_Rejects_ForTamperedOrGarbageText()
    {
        var (_, publicKeyBytes) = GenerateTestKeyPair();
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-peer-dst-{Guid.NewGuid():N}.json");

        var adopted = LicenseImporter.TryAdoptFromPeer("kein Lizenzschlüssel", Guid.NewGuid(), destinationPath, publicKeyBytes, currentLicense: null);

        Assert.False(adopted);
        Assert.False(File.Exists(destinationPath));
    }

    // --------------------------------------- LicenseImporter.ImportFromKeyText - Paketwechsel (Issue #94) ---
    // "Lizenz einspielen" für einen frühzeitigen Upgrade/Downgrade, siehe LicensePackageComparer.

    private static License MakeSignedWithLimit(Guid customerGroupId, int? userLimit, Ed25519PrivateKeyParameters privateKey) =>
        SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)) with { UserLimit = userLimit }, privateKey);

    [Fact]
    public void ImportFromKeyText_QueuesPendingDowngrade_WhenCurrentLicenseIsValidAndNewOneHasFewerDeviceLicenses()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-pkgchange-dst-{Guid.NewGuid():N}.json");
        var pendingPath = Path.Combine(Path.GetTempPath(), $"lizenz-pkgchange-pending-{Guid.NewGuid():N}.json");
        var current = MakeSignedWithLimit(customerGroupId, 25, privateKey);
        File.WriteAllText(destinationPath, System.Text.Json.JsonSerializer.Serialize(current));
        var smaller = MakeSignedWithLimit(customerGroupId, 10, privateKey);

        var diagnosis = LicenseImporter.ImportFromKeyText(LicenseKeyText.Encode(smaller), customerGroupId, destinationPath, pendingPath, publicKeyBytes);

        Assert.Equal(LicenseImportOutcome.PendingDowngrade, diagnosis.Outcome);
        Assert.Equal(10, diagnosis.License!.UserLimit);
        Assert.Equal(25, diagnosis.CurrentLicense!.UserLimit);
        Assert.True(File.Exists(pendingPath));

        // Die bisherige aktive Lizenz bleibt unangetastet aktiv.
        var stillActive = LicenseReader.Load(destinationPath, customerGroupId, publicKeyBytes);
        Assert.Equal(25, stillActive.License!.UserLimit);
    }

    [Fact]
    public void ImportFromKeyText_ActivatesImmediately_ForUpgrade_EvenWhenCurrentLicenseIsValid()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-pkgchange-dst-{Guid.NewGuid():N}.json");
        var pendingPath = Path.Combine(Path.GetTempPath(), $"lizenz-pkgchange-pending-{Guid.NewGuid():N}.json");
        File.WriteAllText(destinationPath, System.Text.Json.JsonSerializer.Serialize(MakeSignedWithLimit(customerGroupId, 25, privateKey)));
        var bigger = MakeSignedWithLimit(customerGroupId, 75, privateKey);

        var diagnosis = LicenseImporter.ImportFromKeyText(LicenseKeyText.Encode(bigger), customerGroupId, destinationPath, pendingPath, publicKeyBytes);

        Assert.Equal(LicenseImportOutcome.Activated, diagnosis.Outcome);
        var active = LicenseReader.Load(destinationPath, customerGroupId, publicKeyBytes);
        Assert.Equal(75, active.License!.UserLimit);
        Assert.False(File.Exists(pendingPath));
    }

    [Fact]
    public void ImportFromKeyText_ActivatesImmediately_ForSmallerPackage_WhenNoValidCurrentLicenseExists()
    {
        // Nutzerentscheidung 08.09.2026: fehlt eine gültige Vorgänger-Lizenz (hier: gar
        // keine Datei vorhanden -> Missing), wird jede neue Lizenz sofort aktiv, egal wie
        // groß das Paket ist.
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-pkgchange-dst-{Guid.NewGuid():N}.json");
        var pendingPath = Path.Combine(Path.GetTempPath(), $"lizenz-pkgchange-pending-{Guid.NewGuid():N}.json");
        var small = MakeSignedWithLimit(customerGroupId, 10, privateKey);

        var diagnosis = LicenseImporter.ImportFromKeyText(LicenseKeyText.Encode(small), customerGroupId, destinationPath, pendingPath, publicKeyBytes);

        Assert.Equal(LicenseImportOutcome.Activated, diagnosis.Outcome);
        Assert.True(File.Exists(destinationPath));
    }

    [Fact]
    public void ImportFromKeyText_ClearsStalePendingFile_WhenSubsequentImportIsUpgrade()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var destinationPath = Path.Combine(Path.GetTempPath(), $"lizenz-pkgchange-dst-{Guid.NewGuid():N}.json");
        var pendingPath = Path.Combine(Path.GetTempPath(), $"lizenz-pkgchange-pending-{Guid.NewGuid():N}.json");
        File.WriteAllText(destinationPath, System.Text.Json.JsonSerializer.Serialize(MakeSignedWithLimit(customerGroupId, 25, privateKey)));
        File.WriteAllText(pendingPath, "irgendeine-alte-vormerkung");

        var bigger = MakeSignedWithLimit(customerGroupId, 75, privateKey);
        var diagnosis = LicenseImporter.ImportFromKeyText(LicenseKeyText.Encode(bigger), customerGroupId, destinationPath, pendingPath, publicKeyBytes);

        Assert.Equal(LicenseImportOutcome.Activated, diagnosis.Outcome);
        Assert.False(File.Exists(pendingPath));
    }

    // ----------------------------------- LicenseImporter.TryAdoptPendingFromPeer (Issue #94) ---

    [Fact]
    public void TryAdoptPendingFromPeer_Adopts_WhenNoPendingHeldYet_AndStillADowngradeAgainstOwnActiveLicense()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var pendingPath = Path.Combine(Path.GetTempPath(), $"lizenz-pending-peer-{Guid.NewGuid():N}.json");
        var ownActive = MakeSignedWithLimit(customerGroupId, 25, privateKey);
        var peerPending = MakeSignedWithLimit(customerGroupId, 10, privateKey);

        var adopted = LicenseImporter.TryAdoptPendingFromPeer(
            LicenseKeyText.Encode(peerPending), customerGroupId, pendingPath, publicKeyBytes, ownActiveLicense: ownActive, ownPendingLicense: null);

        Assert.True(adopted);
        Assert.True(File.Exists(pendingPath));
    }

    [Fact]
    public void TryAdoptPendingFromPeer_Rejects_WhenNoLongerADowngradeAgainstOwnActiveLicense()
    {
        // Die eigene aktive Lizenz ist (per Gossip) längst auf denselben kleinen Stand
        // gewechselt - die vom Peer gemeldete Vormerkung wäre nur noch eine Karteileiche.
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var pendingPath = Path.Combine(Path.GetTempPath(), $"lizenz-pending-peer-{Guid.NewGuid():N}.json");
        var ownActive = MakeSignedWithLimit(customerGroupId, 10, privateKey);
        var peerPending = MakeSignedWithLimit(customerGroupId, 10, privateKey);

        var adopted = LicenseImporter.TryAdoptPendingFromPeer(
            LicenseKeyText.Encode(peerPending), customerGroupId, pendingPath, publicKeyBytes, ownActiveLicense: ownActive, ownPendingLicense: null);

        Assert.False(adopted);
        Assert.False(File.Exists(pendingPath));
    }

    [Fact]
    public void TryAdoptPendingFromPeer_Rejects_WhenPeerPendingIsNotNewerThanOwnPending()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var customerGroupId = Guid.NewGuid();
        var pendingPath = Path.Combine(Path.GetTempPath(), $"lizenz-pending-peer-{Guid.NewGuid():N}.json");
        var ownPending = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)) with { UserLimit = 10, IssuedAtUtc = DateTime.UtcNow }, privateKey);
        var olderPeerPending = SignLicense(MakeUnsigned(customerGroupId, DateTime.UtcNow.AddYears(1)) with { UserLimit = 10, IssuedAtUtc = DateTime.UtcNow.AddDays(-10) }, privateKey);

        var adopted = LicenseImporter.TryAdoptPendingFromPeer(
            LicenseKeyText.Encode(olderPeerPending), customerGroupId, pendingPath, publicKeyBytes, ownActiveLicense: null, ownPendingLicense: ownPending);

        Assert.False(adopted);
    }
}
