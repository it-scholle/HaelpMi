using HaelpMi.Core.Models;
using HaelpMi.Core.Security;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Covers das vierte kryptografische Schlüsselpaar (Ed25519, Admin-Rollen-Signatur,
/// Nutzerwunsch 17.08.2026 - CLAUDE.md "Lizenz &amp; Secrets"): AdminRoleSigner/Verifier
/// (reine Kryptologik, gleiches Muster wie DeviceIdentitySigner/Verifier, siehe
/// DeviceIdentitySecurityTests) und AdminRoleTrustStore (Migrationspfad: Selbst-Erzeugung,
/// TOFU-Pinning, Herkunfts-Vorrang).
/// </summary>
public class AdminRoleSecurityTests
{
    // ---------------------------------------------------------------------
    // AdminRoleSigner / AdminRoleVerifier: reine Kryptologik
    // ---------------------------------------------------------------------

    [Fact]
    public void SignAndVerify_RoundTrips_WithMatchingKeyPair()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;

        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, deviceId, sentAtUtc);

        Assert.NotNull(signature);
        Assert.True(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void TrySign_ReturnsNull_ForUserRole()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();

        var signature = AdminRoleSigner.TrySign(Role.User, Guid.NewGuid(), keyPair.PrivateKeyBase64, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Null(signature); // ein User-Gerät kann nie eine Admin-Behauptung signieren, egal welcher Schlüssel vorliegt
    }

    [Fact]
    public void TrySign_ReturnsNull_WhenPrivateKeyIsMissing()
    {
        var signature = AdminRoleSigner.TrySign(Role.Admin, Guid.NewGuid(), null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Null(signature); // Migrationsfall: Admin-Gerät ohne (noch) verfügbaren Schlüssel signiert einfach nicht
    }

    [Fact]
    public void TrySign_ReturnsNull_WhenPrivateKeyIsCorrupt()
    {
        var signature = AdminRoleSigner.TrySign(Role.Admin, Guid.NewGuid(), "not-valid-base64", Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Null(signature); // darf nie werfen, nur "kann nicht signieren"
    }

    [Fact]
    public void Verify_Rejects_WhenPublicKeyDoesNotMatch()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var wrongKeyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, deviceId, sentAtUtc);

        Assert.False(AdminRoleVerifier.Verify(wrongKeyPair.PublicKeyBase64, customerGroupId, deviceId, sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void Verify_Rejects_WhenDeviceIdWasSwapped()
    {
        // Eine gültige Signatur für Gerät A darf nicht auch für Gerät B gelten - genau das
        // verhindert, dass DeviceId Teil der signierten Nutzlast ist (AdminRoleClaim).
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, Guid.NewGuid(), sentAtUtc);

        Assert.False(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, Guid.NewGuid(), sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void Verify_Rejects_WhenCustomerGroupIdWasSwapped()
    {
        // Verhindert, dass eine für einen anderen Kreis gültige Signatur hier akzeptiert
        // würde - zusätzlich zur ohnehin vorgeschalteten CustomerGroupFilter-Prüfung.
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = AdminRoleSigner.TrySign(Role.Admin, Guid.NewGuid(), keyPair.PrivateKeyBase64, deviceId, sentAtUtc);

        Assert.False(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, Guid.NewGuid(), deviceId, sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void Verify_Rejects_WhenTooOld()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow - FreshnessWindow.MaxAge - TimeSpan.FromMinutes(1);
        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, deviceId, sentAtUtc);

        Assert.False(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, sentAtUtc, signature, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Verify_Rejects_WhenTimestampIsInTheFuture()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(30); // Uhr falsch/manipuliert
        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, deviceId, sentAtUtc);

        Assert.False(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, sentAtUtc, signature, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Verify_Rejects_WhenNoPublicKeyIsKnown()
    {
        Assert.False(AdminRoleVerifier.Verify(null, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "irgendeine-signatur", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Verify_Rejects_MalformedSignature()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;

        Assert.False(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, sentAtUtc, "das-ist-keine-gueltige-signatur!!", sentAtUtc));
    }

    // ---------------------------------------------------------------------
    // AdminRoleTrustStore: Migrationspfad (Selbst-Erzeugung, TOFU-Pinning, Herkunft)
    // ---------------------------------------------------------------------

    [Fact]
    public void EnsureSelfGeneratedKeyIfNeeded_GeneratesAndPinsOwnKey_ForAdminWithoutAnyKey()
    {
        using var tempRoot = new TestAppDataScope();
        AdminRoleTrustStore.ResetCacheForTests();

        AdminRoleTrustStore.EnsureSelfGeneratedKeyIfNeeded(Role.Admin, installerPrivateKeyBase64: null);
        var trust = AdminRoleTrustStore.Load();

        Assert.NotEmpty(trust.OwnPublicKeyBase64 ?? "");
        Assert.NotEmpty(trust.OwnPrivateKeyBase64 ?? "");
        Assert.Equal(AdminRoleTrustStore.PrivateKeyProvenance.SelfGenerated, trust.OwnKeyProvenance);
        // Der frisch erzeugte eigene Schlüssel ist bis zu einer Konvergenz auch die eigene
        // Vorstellung vom Gruppenschlüssel (siehe Klassendoku) - beide Werte müssen also
        // übereinstimmen, kein zweiter, unabhängiger Verify-Pfad für das eigene Gerät.
        Assert.Equal(trust.OwnPublicKeyBase64, trust.PinnedGroupPublicKeyBase64);
    }

    [Fact]
    public void EnsureSelfGeneratedKeyIfNeeded_NoOp_ForUserRole()
    {
        using var tempRoot = new TestAppDataScope();
        AdminRoleTrustStore.ResetCacheForTests();

        AdminRoleTrustStore.EnsureSelfGeneratedKeyIfNeeded(Role.User, installerPrivateKeyBase64: null);

        Assert.False(File.Exists(AppPaths.AdminRoleTrustFilePath)); // ein User-Gerät legt nie einen eigenen Signierschlüssel an
    }

    [Fact]
    public void EnsureSelfGeneratedKeyIfNeeded_NoOp_WhenInstallerKeyAlreadyPresent()
    {
        using var tempRoot = new TestAppDataScope();
        AdminRoleTrustStore.ResetCacheForTests();

        // Installer-Herkunft (deployment.json) hat immer Vorrang - der Migrationspfad darf
        // sich dann gar nicht erst einmischen.
        AdminRoleTrustStore.EnsureSelfGeneratedKeyIfNeeded(Role.Admin, installerPrivateKeyBase64: "irgendein-installer-schluessel");

        Assert.False(File.Exists(AppPaths.AdminRoleTrustFilePath));
    }

    [Fact]
    public void EnsureSelfGeneratedKeyIfNeeded_NoOp_WhenOwnKeyAlreadyStored()
    {
        using var tempRoot = new TestAppDataScope();
        AdminRoleTrustStore.ResetCacheForTests();

        var keyPair = AdminRoleSigner.GenerateKeyPair();
        AdminRoleTrustStore.SaveOwnKey(keyPair.PublicKeyBase64, keyPair.PrivateKeyBase64, AdminRoleTrustStore.PrivateKeyProvenance.SelfGenerated);

        AdminRoleTrustStore.EnsureSelfGeneratedKeyIfNeeded(Role.Admin, installerPrivateKeyBase64: null);
        var trust = AdminRoleTrustStore.Load();

        Assert.Equal(keyPair.PublicKeyBase64, trust.OwnPublicKeyBase64); // nicht durch ein zweites, neues Paar ersetzt
    }

    [Fact]
    public void PinGroupPublicKeyIfUnset_PinsOnFirstCall()
    {
        using var tempRoot = new TestAppDataScope();
        AdminRoleTrustStore.ResetCacheForTests();

        AdminRoleTrustStore.PinGroupPublicKeyIfUnset("erster-gesehener-schluessel");
        var trust = AdminRoleTrustStore.Load();

        Assert.Equal("erster-gesehener-schluessel", trust.PinnedGroupPublicKeyBase64);
    }

    [Fact]
    public void PinGroupPublicKeyIfUnset_DoesNotOverwrite_ADifferentLaterKey()
    {
        // Trust-on-First-Use: ein später gemeldeter abweichender Schlüssel wird verworfen,
        // nicht stillschweigend übernommen (identisches Prinzip wie beim
        // Geräte-Identitätspin, DiscoveryService.HandleDatagramAsync).
        using var tempRoot = new TestAppDataScope();
        AdminRoleTrustStore.ResetCacheForTests();

        AdminRoleTrustStore.PinGroupPublicKeyIfUnset("erster-schluessel");
        AdminRoleTrustStore.PinGroupPublicKeyIfUnset("zweiter-abweichender-schluessel");
        var trust = AdminRoleTrustStore.Load();

        Assert.Equal("erster-schluessel", trust.PinnedGroupPublicKeyBase64);
    }

    [Fact]
    public void SaveOwnKey_ReplacesPreviousKey_AndUpdatesPinTogether()
    {
        // Konvergenz-Fall: ein Peer mit kleinerem Schlüssel gewinnt, das eigene (ersetzbare)
        // Paar wird komplett übernommen - Provenance UND gepinnter Gruppenschlüssel ziehen
        // dabei gemeinsam nach, kein Auseinanderlaufen der beiden Felder.
        using var tempRoot = new TestAppDataScope();
        AdminRoleTrustStore.ResetCacheForTests();

        var own = AdminRoleSigner.GenerateKeyPair();
        AdminRoleTrustStore.SaveOwnKey(own.PublicKeyBase64, own.PrivateKeyBase64, AdminRoleTrustStore.PrivateKeyProvenance.SelfGenerated);

        var adopted = AdminRoleSigner.GenerateKeyPair();
        AdminRoleTrustStore.SaveOwnKey(adopted.PublicKeyBase64, adopted.PrivateKeyBase64, AdminRoleTrustStore.PrivateKeyProvenance.PeerAdopted);
        var trust = AdminRoleTrustStore.Load();

        Assert.Equal(adopted.PublicKeyBase64, trust.OwnPublicKeyBase64);
        Assert.Equal(adopted.PrivateKeyBase64, trust.OwnPrivateKeyBase64);
        Assert.Equal(AdminRoleTrustStore.PrivateKeyProvenance.PeerAdopted, trust.OwnKeyProvenance);
        Assert.Equal(adopted.PublicKeyBase64, trust.PinnedGroupPublicKeyBase64);
    }

    [Fact]
    public void Load_ReturnsSameState_AcrossReload()
    {
        using var tempRoot = new TestAppDataScope();
        AdminRoleTrustStore.ResetCacheForTests();
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        AdminRoleTrustStore.SaveOwnKey(keyPair.PublicKeyBase64, keyPair.PrivateKeyBase64, AdminRoleTrustStore.PrivateKeyProvenance.SelfGenerated);

        AdminRoleTrustStore.ResetCacheForTests(); // simuliert einen Neustart des Prozesses
        var reloaded = AdminRoleTrustStore.Load();

        Assert.Equal(keyPair.PublicKeyBase64, reloaded.OwnPublicKeyBase64);
        Assert.Equal(keyPair.PrivateKeyBase64, reloaded.OwnPrivateKeyBase64);
    }

    [Fact]
    public void Load_ToleratesCorruptFile()
    {
        using var tempRoot = new TestAppDataScope();
        AdminRoleTrustStore.ResetCacheForTests();
        AppPaths.EnsureRootExists();
        File.WriteAllText(AppPaths.AdminRoleTrustFilePath, "das ist kein JSON");

        var trust = AdminRoleTrustStore.Load(); // darf nicht werfen

        Assert.Null(trust.OwnPublicKeyBase64);
    }
}
