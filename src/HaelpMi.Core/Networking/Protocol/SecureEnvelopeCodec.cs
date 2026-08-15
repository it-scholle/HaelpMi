using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HaelpMi.Core.Security;
using Org.BouncyCastle.Crypto.Parameters;
// Namensalias gegen die Mehrdeutigkeit mit System.Security.Cryptography.ChaCha20Poly1305
// (.NET-Eigenimplementierung, auf älteren Windows-Ständen ohne CNG-Unterstützung nicht
// garantiert verfügbar - siehe HaelpMi.Core.csproj-Kommentar bei BouncyCastle: gleicher
// Grund, aus dem schon Ed25519 nicht die .NET-eigene Variante nutzt).
using BouncyChaCha20Poly1305 = Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305;

namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Versiegelt/öffnet die Nutzlast eines SecureEnvelope: ChaCha20-Poly1305 (BouncyCastle,
/// bereits Abhängigkeit für die Ed25519-Signaturen - kein zweites Krypto-Paket) mit dem
/// gruppenweiten symmetrischen Schlüssel (DeploymentInfo.GroupKeyBase64), plus eine
/// Ed25519-Signatur des sendenden Geräts über den Klartext (Sign-then-Encrypt - siehe
/// unten für die Reihenfolge-Begründung). Von allen Kanälen gemeinsam genutzt, die eine
/// Klartext-Nachricht schützen wollen (Alarm, Antwort-Kanal, Config-Sync-Pull,
/// Audit-Sync).
///
/// Nonce-Wiederverwendung: unbedenklich bei einem zufälligen 96-Bit-Nonce pro Nachricht,
/// auch gruppenweit über Jahre hinweg - die Geburtstagsgrenze für ChaCha20-Poly1305
/// liegt bei rund 2^48 Nachrichten unter einem Schlüssel; selbst eine pessimistische
/// Zehn-Jahres-Abschätzung für einen einzelnen Kreis (100 Alarme/Tag * 60 Wiederholungen
/// * 50 Zielgeräte * 3650 Tage) landet bei rund 2^30 - achtzehn Größenordnungen darunter.
/// Kein persistenter Nonce-Zähler nötig.
///
/// Empfangsreihenfolge in TryOpen ist bewusst billigste-Prüfung-zuerst: der AEAD-Tag
/// (beweist nur Gruppenmitgliedschaft, jedes Gerät im Kreis teilt denselben
/// Gruppenschlüssel) wird VOR der teuren Ed25519-Verifikation geprüft (die erst die
/// Identität des konkreten Absender-Geräts beweist) - ein Angreifer ohne
/// Gruppenschlüssel kann so nie teure Asymmetrie-Operationen erzwingen, egal wie viel
/// Müll er schickt.
///
/// Sign-then-Encrypt statt Encrypt-then-Sign: die Signatur landet dadurch INNERHALB des
/// verschlüsselten Klartexts (ein Mitlauscher sieht sie nie unverschlüsselt), und die
/// Empfangsreihenfolge oben (AEAD zuerst) bleibt eine einzige billige-zuerst-Prüfkette,
/// ohne eine zweite, äußere Signatur über den Ciphertext separat verwalten zu müssen.
/// </summary>
internal static class SecureEnvelopeCodec
{
    private const int EnvelopeFormatVersion = 1;
    private const int NonceLengthBytes = 12; // 96 Bit, ChaCha20-Poly1305-Standardgröße
    private const int MacSizeBits = 128;

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record InnerEnvelope(string BodyJsonBase64, DateTimeOffset SentAtUtc, string SignatureBase64);

    /// <summary>
    /// Verschlüsselt+signiert <paramref name="body"/>. Gibt <c>null</c> zurück, wenn
    /// lokal nicht signiert werden kann (kaputter Geräte-Schlüssel, siehe
    /// DeviceIdentitySigner) oder kein Gruppenschlüssel vorliegt (alter Installer-Stand
    /// ohne GroupKeyBase64, siehe DeploymentInfo) - der Aufrufer behandelt das wie
    /// "Peer/wir selbst nicht verschlüsselungsfähig" und fällt auf das bisherige
    /// Klartextformat zurück.
    /// </summary>
    public static SecureEnvelope? Seal<T>(T body, Guid customerGroupId, Guid deviceId, string? groupKeyBase64, string devicePrivateKeyBase64, DateTimeOffset sentAtUtc)
    {
        if (string.IsNullOrEmpty(groupKeyBase64))
        {
            return null;
        }

        try
        {
            var groupKey = Convert.FromBase64String(groupKeyBase64);
            var nonce = RandomNumberGenerator.GetBytes(NonceLengthBytes);
            var bodyJsonBytes = JsonSerializer.SerializeToUtf8Bytes(body, Options);

            var signatureBase64 = DeviceIdentitySigner.TrySign(devicePrivateKeyBase64, customerGroupId, deviceId, nonce, bodyJsonBytes, sentAtUtc);
            if (signatureBase64 is null)
            {
                return null;
            }

            var inner = new InnerEnvelope(Convert.ToBase64String(bodyJsonBytes), sentAtUtc, signatureBase64);
            var innerPlaintext = JsonSerializer.SerializeToUtf8Bytes(inner, Options);

            var associatedData = BuildAssociatedData(customerGroupId, deviceId);
            var ciphertext = Encrypt(groupKey, nonce, innerPlaintext, associatedData);

            return new SecureEnvelope(EnvelopeFormatVersion, customerGroupId, deviceId, Convert.ToBase64String(nonce), Convert.ToBase64String(ciphertext));
        }
        catch (Exception)
        {
            // Kaputter Base64-Gruppenschlüssel o. ä. darf das Senden nie zum Absturz
            // bringen - dann eben nicht verschlüsselt versendbar, siehe Klassendoku.
            return null;
        }
    }

    /// <summary>
    /// Entschlüsselt+verifiziert einen SecureEnvelope. Gibt <c>null</c> bei JEDER Art von
    /// Fehlschlag zurück (falscher Gruppenschlüssel, manipulierter Ciphertext, fehlender
    /// oder falscher gepinnter Geräte-Schlüssel, abgelaufener/zukünftiger Zeitstempel,
    /// kaputtes JSON) - bewusst nicht von außen unterscheidbar, gleiche stille-Drop-
    /// Philosophie wie CustomerGroupFilter.
    /// </summary>
    public static T? TryOpen<T>(SecureEnvelope envelope, string? groupKeyBase64, string? pinnedDevicePublicKeyBase64, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrEmpty(groupKeyBase64))
        {
            return default;
        }

        try
        {
            var groupKey = Convert.FromBase64String(groupKeyBase64);
            var nonce = Convert.FromBase64String(envelope.NonceBase64);
            if (nonce.Length != NonceLengthBytes)
            {
                return default;
            }

            var ciphertext = Convert.FromBase64String(envelope.CiphertextBase64);
            var associatedData = BuildAssociatedData(envelope.CustomerGroupId, envelope.DeviceId);
            var innerPlaintext = Decrypt(groupKey, nonce, ciphertext, associatedData);

            var inner = JsonSerializer.Deserialize<InnerEnvelope>(innerPlaintext, Options);
            if (inner is null)
            {
                return default;
            }

            var bodyJsonBytes = Convert.FromBase64String(inner.BodyJsonBase64);
            var verified = DeviceIdentityVerifier.Verify(pinnedDevicePublicKeyBase64, envelope.CustomerGroupId, envelope.DeviceId, nonce, bodyJsonBytes, inner.SentAtUtc, inner.SignatureBase64, nowUtc);
            if (!verified)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(bodyJsonBytes, Options);
        }
        catch (Exception)
        {
            // Untrusted network input: ein AEAD-Tag-Fehlschlag (BouncyCastle wirft
            // InvalidCipherTextException), kaputtes Base64/JSON o. ä. sind hier alle
            // gleichwertig "nicht zu öffnen" - nie eine Exception nach außen durchreichen.
            return default;
        }
    }

    private static byte[] BuildAssociatedData(Guid customerGroupId, Guid deviceId) =>
        Encoding.UTF8.GetBytes($"{customerGroupId:D}|{deviceId:D}");

    private static byte[] Encrypt(byte[] key, byte[] nonce, byte[] plaintext, byte[] associatedData)
    {
        var cipher = new BouncyChaCha20Poly1305();
        cipher.Init(true, new AeadParameters(new KeyParameter(key), MacSizeBits, nonce, associatedData));
        var output = new byte[cipher.GetOutputSize(plaintext.Length)];
        var len = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
        len += cipher.DoFinal(output, len);
        return output[..len];
    }

    private static byte[] Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] associatedData)
    {
        var cipher = new BouncyChaCha20Poly1305();
        cipher.Init(false, new AeadParameters(new KeyParameter(key), MacSizeBits, nonce, associatedData));
        var output = new byte[cipher.GetOutputSize(ciphertext.Length)];
        var len = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
        len += cipher.DoFinal(output, len);
        return output[..len];
    }
}
