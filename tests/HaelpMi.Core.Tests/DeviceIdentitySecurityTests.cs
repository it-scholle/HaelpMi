using HaelpMi.Core.Security;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Covers das fünfte kryptografische Geheimnis (Ed25519, Geräte-Identität, laufzeit-
/// generiert - CLAUDE.md "Lizenz &amp; Secrets"): DeviceIdentitySigner/Verifier (reine
/// Kryptologik, gleiches Muster wie AdminRoleSigner/Verifier) und DeviceIdentityStore
/// (DPAPI-Persistenz, Erzeugung beim ersten Aufruf).
/// </summary>
public class DeviceIdentitySecurityTests
{
    private static byte[] Body() => "{\"text\":\"Testalarm\"}"u8.ToArray();

    // ---------------------------------------------------------------------
    // DeviceIdentitySigner / DeviceIdentityVerifier: reine Kryptologik
    // ---------------------------------------------------------------------

    [Fact]
    public void SignAndVerify_RoundTrips_WithMatchingKeyPair()
    {
        var keyPair = DeviceIdentitySigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var nonce = new byte[12];
        var sentAtUtc = DateTimeOffset.UtcNow;
        var body = Body();

        var signature = DeviceIdentitySigner.TrySign(keyPair.PrivateKeyBase64, customerGroupId, deviceId, nonce, body, sentAtUtc);

        Assert.NotNull(signature);
        Assert.True(DeviceIdentityVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, nonce, body, sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void TrySign_ReturnsNull_WhenPrivateKeyIsCorrupt()
    {
        var signature = DeviceIdentitySigner.TrySign("not-valid-base64-key", Guid.NewGuid(), Guid.NewGuid(), new byte[12], Body(), DateTimeOffset.UtcNow);

        Assert.Null(signature); // ein kaputter lokaler Schlüssel darf nie werfen, nur "kann nicht signieren"
    }

    [Fact]
    public void Verify_Rejects_WhenPublicKeyDoesNotMatch()
    {
        var keyPair = DeviceIdentitySigner.GenerateKeyPair();
        var wrongKeyPair = DeviceIdentitySigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var nonce = new byte[12];
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = DeviceIdentitySigner.TrySign(keyPair.PrivateKeyBase64, customerGroupId, deviceId, nonce, Body(), sentAtUtc);

        Assert.False(DeviceIdentityVerifier.Verify(wrongKeyPair.PublicKeyBase64, customerGroupId, deviceId, nonce, Body(), sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void Verify_Rejects_WhenDeviceIdWasSwapped()
    {
        // Eine gültige Signatur für Gerät A darf nicht auch für Gerät B gelten - genau das
        // verhindert, dass DeviceId Teil der signierten Nutzlast ist (DeviceIdentityClaim).
        var keyPair = DeviceIdentitySigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var nonce = new byte[12];
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = DeviceIdentitySigner.TrySign(keyPair.PrivateKeyBase64, customerGroupId, Guid.NewGuid(), nonce, Body(), sentAtUtc);

        Assert.False(DeviceIdentityVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, Guid.NewGuid(), nonce, Body(), sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void Verify_Rejects_WhenCustomerGroupIdWasSwapped()
    {
        var keyPair = DeviceIdentitySigner.GenerateKeyPair();
        var deviceId = Guid.NewGuid();
        var nonce = new byte[12];
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = DeviceIdentitySigner.TrySign(keyPair.PrivateKeyBase64, Guid.NewGuid(), deviceId, nonce, Body(), sentAtUtc);

        Assert.False(DeviceIdentityVerifier.Verify(keyPair.PublicKeyBase64, Guid.NewGuid(), deviceId, nonce, Body(), sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void Verify_Rejects_WhenBodyWasTampered()
    {
        var keyPair = DeviceIdentitySigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var nonce = new byte[12];
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = DeviceIdentitySigner.TrySign(keyPair.PrivateKeyBase64, customerGroupId, deviceId, nonce, Body(), sentAtUtc);

        var tamperedBody = "{\"text\":\"Manipuliert\"}"u8.ToArray();
        Assert.False(DeviceIdentityVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, nonce, tamperedBody, sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void Verify_Rejects_WhenTooOld()
    {
        var keyPair = DeviceIdentitySigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var nonce = new byte[12];
        var sentAtUtc = DateTimeOffset.UtcNow - FreshnessWindow.MaxAge - TimeSpan.FromMinutes(1);
        var signature = DeviceIdentitySigner.TrySign(keyPair.PrivateKeyBase64, customerGroupId, deviceId, nonce, Body(), sentAtUtc);

        Assert.False(DeviceIdentityVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, nonce, Body(), sentAtUtc, signature, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Verify_Rejects_WhenTimestampIsInTheFuture()
    {
        var keyPair = DeviceIdentitySigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var nonce = new byte[12];
        var sentAtUtc = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(30); // Uhr falsch/manipuliert
        var signature = DeviceIdentitySigner.TrySign(keyPair.PrivateKeyBase64, customerGroupId, deviceId, nonce, Body(), sentAtUtc);

        Assert.False(DeviceIdentityVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, nonce, Body(), sentAtUtc, signature, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Verify_Rejects_WhenNoPinnedKeyIsKnown()
    {
        Assert.False(DeviceIdentityVerifier.Verify(null, Guid.NewGuid(), Guid.NewGuid(), new byte[12], Body(), DateTimeOffset.UtcNow, "irgendeine-signatur", DateTimeOffset.UtcNow));
    }

    // ---------------------------------------------------------------------
    // DeviceIdentityStore: Persistenz/Erzeugung
    // ---------------------------------------------------------------------

    [Fact]
    public void LoadOrCreate_GeneratesAndPersists_OnFirstCall()
    {
        using var tempRoot = new TestAppDataScope();
        DeviceIdentityStore.ResetCacheForTests();

        var keyPair = DeviceIdentityStore.LoadOrCreate();

        Assert.NotEmpty(keyPair.PublicKeyBase64);
        Assert.NotEmpty(keyPair.PrivateKeyBase64);
        Assert.True(File.Exists(AppPaths.DeviceIdentityFilePath));
    }

    [Fact]
    public void LoadOrCreate_ReturnsSameKeyPair_AcrossReload()
    {
        using var tempRoot = new TestAppDataScope();
        DeviceIdentityStore.ResetCacheForTests();
        var first = DeviceIdentityStore.LoadOrCreate();

        DeviceIdentityStore.ResetCacheForTests(); // simuliert einen Neustart des Prozesses
        var second = DeviceIdentityStore.LoadOrCreate();

        Assert.Equal(first.PublicKeyBase64, second.PublicKeyBase64);
        Assert.Equal(first.PrivateKeyBase64, second.PrivateKeyBase64);
    }

    [Fact]
    public void LoadOrCreate_GeneratesNewKeyPair_WhenStoredFileIsCorrupt()
    {
        using var tempRoot = new TestAppDataScope();
        DeviceIdentityStore.ResetCacheForTests();
        AppPaths.EnsureRootExists();
        File.WriteAllText(AppPaths.DeviceIdentityFilePath, "das ist kein JSON");

        var keyPair = DeviceIdentityStore.LoadOrCreate(); // darf nicht werfen

        Assert.NotEmpty(keyPair.PublicKeyBase64);
    }
}
