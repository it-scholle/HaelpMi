using System.Text.Json;
using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;
using HaelpMi.LicenseSigner;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// FR-32: Ed25519-signierte Lizenzdatei (Kundenname/Sitzanzahl/Ablaufdatum) plus die
/// Eskalations-/Drosselungslogik für Admin-Warnungen. Nutzt (wie UpdatesTests.cs für
/// Updates) ein eigenes Wegwerf-Schlüsselpaar über LicenseSigningOperations - nie den
/// echten eingebetteten Produktionsschlüssel.
/// </summary>
public class LicenseTests
{
    // --- DateOnly-JSON-Round-Trip: im Repo bisher ungenutzter Typ, deshalb zuerst geprüft,
    // bevor irgendetwas anderes darauf aufbaut. ------------------------------------------

    [Fact]
    public void LicenseFile_ExpiresOnUtc_RoundTripsExactlyThroughJson()
    {
        var original = new LicenseFile("Testkunde GmbH", 5, new DateOnly(2026, 12, 31), "sig");

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<LicenseFile>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.ExpiresOnUtc, roundTripped!.ExpiresOnUtc);
        Assert.Equal(original.CustomerName, roundTripped.CustomerName);
        Assert.Equal(original.SeatCount, roundTripped.SeatCount);
    }

    // --- LicenseVerifier: Signaturprüfung -------------------------------------------------

    private static (byte[] PrivateKey, byte[] PublicKeyBytes) GenerateTestKeyPair()
    {
        var pair = LicenseSigningOperations.GenerateKeyPair();
        return (pair.PrivateKey, pair.PublicKey);
    }

    private static LicenseFile SignLicense(string customerName, int seatCount, DateOnly expiresOnUtc, byte[] privateKey)
    {
        var signed = LicenseSigningOperations.Sign(customerName, seatCount, expiresOnUtc, privateKey);
        return new LicenseFile(signed.CustomerName, signed.SeatCount, signed.ExpiresOnUtc, signed.SignatureBase64);
    }

    [Fact]
    public void Verify_AcceptsLicense_SignedWithMatchingPrivateKey()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var license = SignLicense("Testkunde GmbH", 5, new DateOnly(2026, 12, 31), privateKey);

        Assert.True(LicenseVerifier.Verify(license, publicKeyBytes));
    }

    [Fact]
    public void Verify_RejectsLicense_WhenFieldWasTamperedWithAfterSigning()
    {
        var (privateKey, publicKeyBytes) = GenerateTestKeyPair();
        var signed = SignLicense("Testkunde GmbH", 5, new DateOnly(2026, 12, 31), privateKey);

        var tampered = signed with { SeatCount = 500 }; // Sitzanzahl nachträglich erhöht

        Assert.False(LicenseVerifier.Verify(tampered, publicKeyBytes));
    }

    [Fact]
    public void Verify_RejectsLicense_SignedWithADifferentPrivateKey()
    {
        var (_, attackerPublicKeyBytes) = GenerateTestKeyPair();
        var (realPrivateKey, _) = GenerateTestKeyPair();

        var license = SignLicense("Testkunde GmbH", 5, new DateOnly(2026, 12, 31), realPrivateKey);

        Assert.False(LicenseVerifier.Verify(license, attackerPublicKeyBytes));
    }

    [Fact]
    public void Verify_RejectsLicense_WhenSignatureIsNotValidBase64()
    {
        var (_, publicKeyBytes) = GenerateTestKeyPair();
        var license = new LicenseFile("Testkunde GmbH", 5, new DateOnly(2026, 12, 31), "not-valid-base64!!!");

        Assert.False(LicenseVerifier.Verify(license, publicKeyBytes));
    }

    // --- LicenseEvaluator: Eskalationsstufen ---------------------------------------------

    [Theory]
    [InlineData(31, LicenseStanding.Good, 0)] // knapp außerhalb des 30-Tage-Vorlaufs
    [InlineData(30, LicenseStanding.Warning, 1)]
    [InlineData(15, LicenseStanding.Warning, 1)]
    [InlineData(14, LicenseStanding.Warning, 2)]
    [InlineData(8, LicenseStanding.Warning, 2)]
    [InlineData(7, LicenseStanding.Warning, 3)]
    [InlineData(2, LicenseStanding.Warning, 3)]
    [InlineData(1, LicenseStanding.Warning, 4)]
    [InlineData(0, LicenseStanding.Warning, 4)]
    [InlineData(-1, LicenseStanding.NotGood, 5)] // 1 Tag überfällig
    [InlineData(-6, LicenseStanding.NotGood, 5)]
    [InlineData(-7, LicenseStanding.NotGood, 6)]
    [InlineData(-29, LicenseStanding.NotGood, 6)]
    [InlineData(-30, LicenseStanding.NotGood, 7)]
    [InlineData(-89, LicenseStanding.NotGood, 7)]
    [InlineData(-90, LicenseStanding.NotGood, 8)]
    [InlineData(-365, LicenseStanding.NotGood, 8)]
    public void Evaluate_MapsDaysUntilExpiry_ToTheExpectedEscalationStage(int daysUntilExpiry, LicenseStanding expectedStanding, int expectedStage)
    {
        var today = new DateOnly(2026, 1, 1);
        var expiresOn = today.AddDays(daysUntilExpiry);
        var license = new LicenseFile("Testkunde GmbH", 5, expiresOn, "sig");

        var result = LicenseEvaluator.Evaluate(license, today);

        Assert.Equal(expectedStanding, result.Standing);
        Assert.Equal(expectedStage, result.StageIndex);
        Assert.Equal(daysUntilExpiry, result.DaysUntilExpiry);
    }

    [Fact]
    public void Evaluate_MissingLicense_ReturnsHighestStage()
    {
        var result = LicenseEvaluator.Evaluate(null, new DateOnly(2026, 1, 1));

        Assert.Equal(LicenseStanding.NotGood, result.Standing);
        Assert.Equal(LicenseEvaluator.MissingOrInvalidStageIndex, result.StageIndex);
        Assert.Null(result.DaysUntilExpiry);
    }

    // --- LicenseChecker: Drosselung/Eskalation, Fail-open ---------------------------------
    //
    // Kann nur den "fehlende/nicht verifizierbare Lizenz"-Pfad (Stufe 100) direkt über
    // Datei+LicenseChecker prüfen: LicenseFileLoader verifiziert grundsätzlich gegen den
    // fest eingebetteten Produktions-Platzhalterschlüssel (LicensePublicKey.Bytes), zu dem
    // hier kein privater Testschlüssel vorliegt (der bleibt bewusst nie im Repo) - eine mit
    // einem Wegwerf-Schlüsselpaar signierte Testdatei würde ebenfalls als "ungültig" (Stufe
    // 100) verifizieren, nicht als "gut". Die Stufen-Zuordnung selbst ist bereits oben über
    // LicenseEvaluator direkt (ohne Datei/Signatur) vollständig abgedeckt.

    private static OwnSettings NewTestSettings() => new()
    {
        DeviceId = Guid.NewGuid(),
        ComputerName = "TESTGERAET",
    };

    [Fact]
    public void CheckOnce_NotifiesAndPersistsStage_WhenStageIncreasesFromNoLicenseFile()
    {
        using var scope = new TestAppDataScope();
        var settingsStore = new SettingsStore();
        settingsStore.Save(NewTestSettings());
        var auditEntries = new List<string>();
        var checker = new LicenseChecker(settingsStore, auditEntries.Add);

        var (result, shouldNotify) = checker.CheckOnce(new DateOnly(2026, 1, 1));

        Assert.True(shouldNotify);
        Assert.Equal(LicenseEvaluator.MissingOrInvalidStageIndex, result.StageIndex);
        Assert.Equal(LicenseEvaluator.MissingOrInvalidStageIndex, settingsStore.Load().LicenseLastNotifiedStage);
        Assert.Contains(auditEntries, e => e.Contains("Lizenz-Warnstufe erreicht"));
    }

    [Fact]
    public void CheckOnce_DoesNotRenotify_WhenStageIsUnchangedSinceLastCheck()
    {
        using var scope = new TestAppDataScope();
        var settingsStore = new SettingsStore();
        settingsStore.Save(NewTestSettings());
        var checker = new LicenseChecker(settingsStore, _ => { });

        checker.CheckOnce(new DateOnly(2026, 1, 1)); // erster Aufruf: Stufe 0 -> 100, meldet
        var (_, shouldNotifyAgain) = checker.CheckOnce(new DateOnly(2026, 1, 2)); // weiterhin keine Datei: Stufe bleibt 100

        Assert.False(shouldNotifyAgain);
    }

    [Fact]
    public void CheckOnce_FailsOpen_WhenSettingsAreMissingOrBroken()
    {
        using var scope = new TestAppDataScope();
        // Bewusst KEIN settingsStore.Save() vorher - settings.json fehlt, SettingsStore.Load()
        // wirft. Das äußerste catch (Exception) in LicenseChecker.CheckOnce muss das
        // abfangen und einen harmlosen "gut"-Zustand liefern statt den Aufrufer (Agent) zu stören.
        var settingsStore = new SettingsStore();
        var checker = new LicenseChecker(settingsStore, _ => throw new InvalidOperationException("darf hier nie aufgerufen werden"));

        var (result, shouldNotify) = checker.CheckOnce(new DateOnly(2026, 1, 1));

        Assert.False(shouldNotify);
        Assert.Equal(LicenseStanding.Good, result.Standing);
    }
}
