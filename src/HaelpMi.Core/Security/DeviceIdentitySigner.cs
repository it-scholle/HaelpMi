using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace HaelpMi.Core.Security;

/// <summary>
/// Erzeugt und benutzt das fünfte kryptografische Geheimnis des Projekts (Ed25519,
/// Geräte-Identität, CLAUDE.md "Lizenz &amp; Secrets") - anders als die vier
/// produktweiten Schlüsselpaare (Lizenz, Update, Installer-Passwort, Admin-Rolle) wird
/// dieses NICHT vom Install-Creator zur Baubauzeit erzeugt, sondern von jedem Gerät
/// selbst beim allerersten Programmstart (siehe DeviceIdentityStore) - ein rein
/// laufzeit-generiertes, pro-Gerät-Geheimnis, dessen privater Teil das Gerät nie
/// verlässt und dessen öffentlicher Teil erst beim ersten direkten Boot-Call-Kontakt
/// einem Peer bekannt wird (Trust-on-First-Use, siehe DiscoveryService).
///
/// Bewusst ein eigener, von AdminRoleSigner/UpdateSigningOperations getrennter
/// Ed25519-Keygen-/Sign-Aufruf, obwohl die Kryptoprimitive identisch ist - CLAUDE.md
/// "niemals verwechseln oder zusammenlegen" gilt für die Codepfade, nicht nur die
/// Schlüssel selbst.
/// </summary>
public static class DeviceIdentitySigner
{
    public readonly record struct KeyPair(string PublicKeyBase64, string PrivateKeyBase64);

    public static KeyPair GenerateKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();

        var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (Ed25519PublicKeyParameters)keyPair.Public;

        return new KeyPair(Convert.ToBase64String(publicKey.GetEncoded()), Convert.ToBase64String(privateKey.GetEncoded()));
    }

    /// <summary>
    /// Signiert einen SecureEnvelope-Inhalt mit dem lokalen Geräte-Identitätsschlüssel.
    /// Gibt <c>null</c> zurück statt zu werfen, falls der private Schlüssel (z. B. durch
    /// eine beschädigte device-identity.json) nicht brauchbar ist - der Aufrufer
    /// (SecureEnvelopeCodec.Seal) behandelt das wie "kann diese Nachricht gerade nicht
    /// verschlüsselt/signiert verschicken", nicht wie einen Programmfehler.
    /// </summary>
    public static string? TrySign(string devicePrivateKeyBase64, Guid customerGroupId, Guid deviceId, byte[] nonce, byte[] bodyJsonBytes, DateTimeOffset sentAtUtc)
    {
        try
        {
            var privateKeyBytes = Convert.FromBase64String(devicePrivateKeyBase64);
            var hash = SHA256.HashData(DeviceIdentityClaim.BuildPayload(customerGroupId, deviceId, nonce, bodyJsonBytes, sentAtUtc));

            var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes, 0);
            var signer = new Ed25519Signer();
            signer.Init(true, privateKey);
            signer.BlockUpdate(hash, 0, hash.Length);
            return Convert.ToBase64String(signer.GenerateSignature());
        }
        catch (Exception)
        {
            // Ein kaputter/zu kurzer lokaler Schlüssel darf das Senden nie zum Absturz
            // bringen - dann eben nicht signierbar, siehe Klassendoku oben.
            return null;
        }
    }
}
