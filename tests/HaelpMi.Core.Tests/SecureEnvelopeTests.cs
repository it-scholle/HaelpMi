using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Security;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Covers SecureEnvelopeCodec (LAN-Verschlüsselung, siehe CLAUDE.md "Lizenz &amp;
/// Secrets"): Seal/TryOpen-Roundtrip, Manipulationserkennung (falscher Gruppenschlüssel,
/// manipulierter Ciphertext, falscher gepinnter Geräte-Schlüssel), sowie die
/// Nonce-Eindeutigkeit über mehrere Seal-Aufrufe.
/// </summary>
public class SecureEnvelopeTests
{
    private sealed record TestBody(string Text, int Number);

    private static string NewGroupKeyBase64() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void SealAndTryOpen_RoundTrips_WithMatchingKeys()
    {
        var groupKey = NewGroupKeyBase64();
        var device = DeviceIdentitySigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var body = new TestBody("Testalarm", 42);

        var envelope = SecureEnvelopeCodec.Seal(body, customerGroupId, deviceId, groupKey, device.PrivateKeyBase64, DateTimeOffset.UtcNow);
        Assert.NotNull(envelope);

        var opened = SecureEnvelopeCodec.TryOpen<TestBody>(envelope!, groupKey, device.PublicKeyBase64, DateTimeOffset.UtcNow);

        Assert.Equal(body, opened);
    }

    [Fact]
    public void Seal_ReturnsNull_WhenGroupKeyIsMissing()
    {
        var device = DeviceIdentitySigner.GenerateKeyPair();

        var envelope = SecureEnvelopeCodec.Seal(new TestBody("x", 1), Guid.NewGuid(), Guid.NewGuid(), null, device.PrivateKeyBase64, DateTimeOffset.UtcNow);

        Assert.Null(envelope); // alter Installer-Stand ohne GroupKeyBase64 -> Aufrufer fällt auf Klartext zurück
    }

    [Fact]
    public void TryOpen_ReturnsNull_WhenGroupKeyIsWrong()
    {
        var device = DeviceIdentitySigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var envelope = SecureEnvelopeCodec.Seal(new TestBody("x", 1), customerGroupId, deviceId, NewGroupKeyBase64(), device.PrivateKeyBase64, DateTimeOffset.UtcNow);
        Assert.NotNull(envelope);

        var opened = SecureEnvelopeCodec.TryOpen<TestBody>(envelope!, NewGroupKeyBase64(), device.PublicKeyBase64, DateTimeOffset.UtcNow);

        Assert.Null(opened); // zwei unabhängige Kreise dürfen sich nie gegenseitig entschlüsseln können
    }

    [Fact]
    public void TryOpen_ReturnsNull_WhenPinnedDeviceKeyIsWrong()
    {
        var groupKey = NewGroupKeyBase64();
        var device = DeviceIdentitySigner.GenerateKeyPair();
        var impostor = DeviceIdentitySigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var envelope = SecureEnvelopeCodec.Seal(new TestBody("x", 1), customerGroupId, deviceId, groupKey, device.PrivateKeyBase64, DateTimeOffset.UtcNow);
        Assert.NotNull(envelope);

        // Selbst mit korrektem Gruppenschlüssel darf ein falsch gepinnter Geräte-Key nicht durchgehen -
        // die Gruppe allein beweist nur Mitgliedschaft, nicht, welches Gerät konkret gesendet hat.
        var opened = SecureEnvelopeCodec.TryOpen<TestBody>(envelope!, groupKey, impostor.PublicKeyBase64, DateTimeOffset.UtcNow);

        Assert.Null(opened);
    }

    [Fact]
    public void TryOpen_ReturnsNull_WhenNoPinnedKeyIsKnownYet()
    {
        var groupKey = NewGroupKeyBase64();
        var device = DeviceIdentitySigner.GenerateKeyPair();
        var envelope = SecureEnvelopeCodec.Seal(new TestBody("x", 1), Guid.NewGuid(), Guid.NewGuid(), groupKey, device.PrivateKeyBase64, DateTimeOffset.UtcNow);
        Assert.NotNull(envelope);

        var opened = SecureEnvelopeCodec.TryOpen<TestBody>(envelope!, groupKey, null, DateTimeOffset.UtcNow);

        Assert.Null(opened); // noch nie direkten Boot-Call-Kontakt gehabt -> kann nicht verifiziert werden
    }

    [Fact]
    public void TryOpen_ReturnsNull_WhenCiphertextWasTampered()
    {
        var groupKey = NewGroupKeyBase64();
        var device = DeviceIdentitySigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var envelope = SecureEnvelopeCodec.Seal(new TestBody("x", 1), customerGroupId, deviceId, groupKey, device.PrivateKeyBase64, DateTimeOffset.UtcNow);
        Assert.NotNull(envelope);

        var tamperedCiphertext = FlipLastByte(envelope!.CiphertextBase64);
        var tampered = envelope with { CiphertextBase64 = tamperedCiphertext };

        var opened = SecureEnvelopeCodec.TryOpen<TestBody>(tampered, groupKey, device.PublicKeyBase64, DateTimeOffset.UtcNow);

        Assert.Null(opened); // AEAD-Tag muss den Manipulationsversuch erkennen
    }

    [Fact]
    public void TryOpen_ReturnsNull_WhenNonceHasWrongLength()
    {
        var groupKey = NewGroupKeyBase64();
        var device = DeviceIdentitySigner.GenerateKeyPair();
        var envelope = SecureEnvelopeCodec.Seal(new TestBody("x", 1), Guid.NewGuid(), Guid.NewGuid(), groupKey, device.PrivateKeyBase64, DateTimeOffset.UtcNow);
        Assert.NotNull(envelope);

        var tampered = envelope! with { NonceBase64 = Convert.ToBase64String(new byte[4]) };

        var opened = SecureEnvelopeCodec.TryOpen<TestBody>(tampered, groupKey, device.PublicKeyBase64, DateTimeOffset.UtcNow);

        Assert.Null(opened);
    }

    [Fact]
    public void Seal_NeverProducesTheSameNonceTwice()
    {
        var device = DeviceIdentitySigner.GenerateKeyPair();
        var groupKey = NewGroupKeyBase64();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;

        var first = SecureEnvelopeCodec.Seal(new TestBody("x", 1), customerGroupId, deviceId, groupKey, device.PrivateKeyBase64, sentAtUtc);
        var second = SecureEnvelopeCodec.Seal(new TestBody("x", 1), customerGroupId, deviceId, groupKey, device.PrivateKeyBase64, sentAtUtc);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.NonceBase64, second!.NonceBase64);
    }

    private static string FlipLastByte(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        bytes[^1] ^= 0xFF;
        return Convert.ToBase64String(bytes);
    }
}
