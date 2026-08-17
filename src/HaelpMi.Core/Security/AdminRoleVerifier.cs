using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace HaelpMi.Core.Security;

/// <summary>
/// Prüft eine Admin-Rollen-Behauptung aus einem direkten Boot-Call-Kontakt (siehe
/// <c>DiscoveryService.HandleDatagramAsync</c>) gegen den öffentlichen Schlüssel, den
/// dieses Gerät für seine eigene Kunden-Gruppe kennt - entweder aus
/// <see cref="Models.DeploymentInfo.AdminRolePublicKeyBase64"/> (Neuinstallation) oder aus
/// dem TOFU-gepinnten Wert in <see cref="AdminRoleTrustStore"/> (Migrationspfad, siehe
/// dortige Klassendoku). Nur direkter Kontakt wird geprüft - gossip-gelernte Einträge
/// tragen keine Signatur (bewusste Einschränkung, siehe <c>DiscoveryService.
/// AdminPeerContactObserved</c>-Klassendoku für den Hintergrund).
/// </summary>
public static class AdminRoleVerifier
{
    public static bool Verify(string? publicKeyBase64, Guid customerGroupId, Guid deviceId, DateTimeOffset sentAtUtc, string? signatureBase64, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrEmpty(publicKeyBase64) || string.IsNullOrEmpty(signatureBase64))
        {
            return false;
        }

        if (!FreshnessWindow.IsFresh(sentAtUtc, nowUtc))
        {
            return false;
        }

        try
        {
            var publicKeyBytes = Convert.FromBase64String(publicKeyBase64);
            var signature = Convert.FromBase64String(signatureBase64);

            var hash = SHA256.HashData(AdminRoleClaim.BuildPayload(customerGroupId, deviceId, sentAtUtc));
            var publicKey = new Ed25519PublicKeyParameters(publicKeyBytes, 0);
            var verifier = new Ed25519Signer();
            verifier.Init(false, publicKey);
            verifier.BlockUpdate(hash, 0, hash.Length);
            return verifier.VerifySignature(signature);
        }
        catch (Exception)
        {
            // Untrusted network input (CLAUDE.md): ein kaputter/zu kurzer Base64-Wert darf
            // nie eine Exception werfen, nur als "nicht verifiziert" gelten.
            return false;
        }
    }
}
