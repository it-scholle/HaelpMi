using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace HaelpMi.Core.Updates;

/// <summary>
/// "Nur signierte Programm-Updates werden von einem Client angenommen und
/// weiterverteilt" (CLAUDE.md). Verifiziert gegen den in <see cref="UpdateSignaturePublicKey"/>
/// eingebetteten öffentlichen Schlüssel - ein Client hat nie Zugriff auf den privaten
/// Schlüssel und kann daher selbst nie signieren, nur prüfen.
/// </summary>
public static class UpdatePackageVerifier
{
    /// <summary>Prüft gegen den eingebetteten, produktiven öffentlichen Schlüssel - der reguläre Aufrufer.</summary>
    public static bool Verify(byte[] payload, UpdatePackageManifest manifest) =>
        Verify(payload, manifest, UpdateSignaturePublicKey.Bytes);

    /// <summary>
    /// Berechnet den SHA-256-Hash von <paramref name="payload"/> selbst neu und prüft die
    /// Signatur genau darüber - ein manipuliertes oder beschädigtes Paket fällt damit in
    /// einem einzigen Schritt sowohl bei der Integritäts- als auch bei der Echtheitsprüfung
    /// durch, ohne <see cref="UpdatePackageManifest.Sha256Hex"/> separat vergleichen zu müssen.
    /// Der Schlüssel ist ein Parameter (statt fest verdrahtet), damit Tests mit einem
    /// eigenen Wegwerf-Schlüsselpaar arbeiten können, ohne je den echten privaten
    /// Schlüssel zu benötigen (der nie im Repo liegt).
    /// </summary>
    public static bool Verify(byte[] payload, UpdatePackageManifest manifest, byte[] publicKeyBytes)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(manifest.SignatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        var hash = SHA256.HashData(payload);
        var publicKey = new Ed25519PublicKeyParameters(publicKeyBytes, 0);
        var verifier = new Ed25519Signer();
        verifier.Init(false, publicKey);
        verifier.BlockUpdate(hash, 0, hash.Length);
        return verifier.VerifySignature(signature);
    }
}
