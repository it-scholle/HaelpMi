using HaelpMi.Core.Models;
using HaelpMi.Core.Updates;
using HaelpMi.UpdateSigner;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Abschnitt 11: "nur signierte Programm-Updates werden von einem Client angenommen".
/// Nutzt ein eigenes Wegwerf-Schlüsselpaar (nie den echten eingebetteten Produktions-
/// schlüssel), erzeugt/signiert über <see cref="UpdateSigningOperations"/> - dieselbe Klasse,
/// die auch HaelpMi.UpdateSigner (CLI) und HaelpMi.InstallCreator ("Update-Ei", 13.08.2026)
/// verwenden. Damit sind das hier zugleich Round-Trip-Tests für den echten Signierpfad, nicht
/// nur für die Verify-Seite: GenerateKeyPair -&gt; Sign -&gt; <see cref="UpdatePackageVerifier.Verify(byte[], UpdatePackageManifest, byte[])"/>.
/// </summary>
public class UpdatesTests
{
    private static (byte[] PrivateKey, byte[] PublicKeyBytes) GenerateTestKeyPair()
    {
        var pair = UpdateSigningOperations.GenerateKeyPair();
        return (pair.PrivateKey, pair.PublicKey);
    }

    private static UpdatePackageManifest SignPayload(byte[] payload, byte[] privateKey, string version = "1.2.3")
    {
        var manifest = UpdateSigningOperations.Sign(payload, privateKey, version);
        return new UpdatePackageManifest(manifest.Version, manifest.Sha256Hex, manifest.SignatureBase64, manifest.BuiltAtUtc);
    }

    [Fact]
    public void GenerateKeyPair_ProducesStandardEd25519KeyLengths()
    {
        var pair = UpdateSigningOperations.GenerateKeyPair();

        Assert.Equal(32, pair.PrivateKey.Length);
        Assert.Equal(32, pair.PublicKey.Length);
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

    // --- UpdateOrchestrator.IsMyTurn: entfernt am 13.08.2026, wieder eingeführt am
    // 16.08.2026 (Wellen-Rollout) ------------------------------------------------------
    // Zwischen den beiden Daten testete dieser Abschnitt das frühere gestaffelte
    // Freigabekontingent (ApprovedDeviceQuota, admin-gesetzt, "die ersten N Geräte in
    // stabiler ID-Sortierung") und wurde nach dessen ersatzloser Entfernung (CLAUDE.md-
    // Korrektur "Rollout-Freigabe", 11.08.2026) hinfällig. Die 16.08.2026-Korrektur ergänzt
    // das wieder - jetzt mit automatisch aus dem eigenen Geräte-Cache abgeleitetem n statt
    // eines admin-gesetzten Felds, siehe UpdateOrchestrator.IsMyTurn-Klassenkommentar.

    private static LiveIdentity TestIdentity(string deviceId) =>
        new(Guid.NewGuid(), Guid.Parse(deviceId), "PC", "User", "Raum", "1", Role.User, false, "1.0.0", 1);

    private static DeviceEntry TestDevice(string deviceId, string lastKnownProgramVersion) =>
        new() { DeviceId = Guid.Parse(deviceId), LastKnownProgramVersion = lastKnownProgramVersion };

    private static SharedConfig ApprovedConfig(string? approvedVersion) =>
        new() { UpdateRollout = new UpdateRolloutState { ApprovedVersion = approvedVersion } };

    [Fact]
    public void IsMyTurn_ReturnsFalse_WhenNoVersionIsApproved()
    {
        var identity = TestIdentity("00000000-0000-0000-0000-000000000001");
        var devices = new List<DeviceEntry>();

        Assert.False(UpdateOrchestrator.IsMyTurn(identity, ApprovedConfig(null), devices));
    }

    [Fact]
    public void IsMyTurn_ReturnsFalse_WhenNoKnownPeerHasTheApprovedVersionYet()
    {
        // n = 0 (noch kein einziger bekannter Peer auf der freigegebenen Version) blockiert
        // bewusst jeden Versuch - genau die Lücke, die HaelpMi.UpdateBootstrapper füllt.
        var identity = TestIdentity("00000000-0000-0000-0000-000000000001");
        var devices = new List<DeviceEntry> { TestDevice("00000000-0000-0000-0000-000000000002", "1.0.0") };

        Assert.False(UpdateOrchestrator.IsMyTurn(identity, ApprovedConfig("2.0.0"), devices));
    }

    [Fact]
    public void IsMyTurn_ReturnsTrue_ForFirstDeviceInLine_OnceOnePeerHasAlreadyUpdated()
    {
        var identity = TestIdentity("00000000-0000-0000-0000-000000000001"); // niedrigste ID -> zuerst dran
        var devices = new List<DeviceEntry>
        {
            TestDevice("00000000-0000-0000-0000-000000000002", "1.0.0"), // wartet noch
            TestDevice("00000000-0000-0000-0000-000000000003", "2.0.0"), // schon aktualisiert -> n=1
        };

        Assert.True(UpdateOrchestrator.IsMyTurn(identity, ApprovedConfig("2.0.0"), devices));
    }

    [Fact]
    public void IsMyTurn_ReturnsFalse_ForSecondDeviceInLine_WhenOnlyOneSlotIsOpen()
    {
        var identity = TestIdentity("00000000-0000-0000-0000-000000000002"); // zweite Stelle in der Reihenfolge
        var devices = new List<DeviceEntry>
        {
            TestDevice("00000000-0000-0000-0000-000000000001", "1.0.0"), // wartet auch noch, aber vor uns
            TestDevice("00000000-0000-0000-0000-000000000003", "2.0.0"), // schon aktualisiert -> n=1
        };

        Assert.False(UpdateOrchestrator.IsMyTurn(identity, ApprovedConfig("2.0.0"), devices));
    }

    [Fact]
    public void IsMyTurn_WaveWidensAutomatically_AsMorePeersUpdate_NoAdminActionNeeded()
    {
        var identity = TestIdentity("00000000-0000-0000-0000-000000000002"); // zweite Stelle in der Reihenfolge (Rang 1)
        var devices = new List<DeviceEntry>
        {
            TestDevice("00000000-0000-0000-0000-000000000001", "2.0.0"), // schon aktualisiert -> n=1
            TestDevice("00000000-0000-0000-0000-000000000003", "1.0.0"), // wartet noch
            TestDevice("00000000-0000-0000-0000-000000000004", "1.0.0"), // wartet noch
        };
        // n=1 reicht für unseren Rang 1 noch nicht (1 < 1 ist falsch).
        Assert.False(UpdateOrchestrator.IsMyTurn(identity, ApprovedConfig("2.0.0"), devices));

        // Sobald ein WEITERES Gerät aktualisiert hat (n=2, ohne dass WIR selbst schon dran
        // waren und ohne jeden Admin-Klick), sind wir dran.
        devices[1] = TestDevice("00000000-0000-0000-0000-000000000003", "2.0.0");
        Assert.True(UpdateOrchestrator.IsMyTurn(identity, ApprovedConfig("2.0.0"), devices));
    }
}
