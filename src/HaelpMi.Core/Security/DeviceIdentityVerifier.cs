using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace HaelpMi.Core.Security;

/// <summary>
/// Prüft eine Geräte-Identitätssignatur aus einem geöffneten SecureEnvelope gegen den
/// für die sendende DeviceId gepinnten öffentlichen Schlüssel (Trust-on-First-Use,
/// ausschließlich über direkten Boot-Call-Kontakt gepinnt - siehe DiscoveryService,
/// gleiche Einschränkung wie beim Admin-Rollen-Nachweis). Ohne gepinnten Schlüssel kann
/// eine Nachricht grundsätzlich nicht verifiziert werden - das Pinnen selbst passiert
/// ausschließlich im Boot-Call-Pfad, nicht beim Öffnen eines beliebigen Envelopes (kein
/// TOFU-Accept an dieser Stelle).
/// </summary>
public static class DeviceIdentityVerifier
{
    public static bool Verify(string? pinnedPublicKeyBase64, Guid customerGroupId, Guid deviceId, byte[] nonce, byte[] bodyJsonBytes, DateTimeOffset sentAtUtc, string? signatureBase64, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrEmpty(pinnedPublicKeyBase64) || string.IsNullOrEmpty(signatureBase64))
        {
            return false;
        }

        if (!FreshnessWindow.IsFresh(sentAtUtc, nowUtc))
        {
            return false;
        }

        try
        {
            var publicKeyBytes = Convert.FromBase64String(pinnedPublicKeyBase64);
            var signature = Convert.FromBase64String(signatureBase64);

            var hash = SHA256.HashData(DeviceIdentityClaim.BuildPayload(customerGroupId, deviceId, nonce, bodyJsonBytes, sentAtUtc));
            var publicKey = new Ed25519PublicKeyParameters(publicKeyBytes, 0);
            var verifier = new Ed25519Signer();
            verifier.Init(false, publicKey);
            verifier.BlockUpdate(hash, 0, hash.Length);
            return verifier.VerifySignature(signature);
        }
        catch (Exception)
        {
            // Untrusted network input (CLAUDE.md): ein kaputter/zu kurzer Base64-Wert
            // darf nie eine Exception werfen, nur als "nicht verifiziert" gelten.
            return false;
        }
    }
}
