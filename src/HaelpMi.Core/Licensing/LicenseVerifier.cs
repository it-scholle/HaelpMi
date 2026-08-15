using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Prüft die Ed25519-Signatur einer <see cref="LicenseFile"/> gegen den in
/// <see cref="LicensePublicKey"/> eingebetteten öffentlichen Schlüssel - ein Client hat
/// nie Zugriff auf den privaten Schlüssel und kann daher selbst nie signieren, nur prüfen.
/// Spiegelt bewusst exakt das bestehende Muster von
/// HaelpMi.Core/Updates/UpdatePackageVerifier.cs (gleiche Bibliothek, gleicher
/// Hash-dann-signieren-Ablauf), damit beide Ed25519-Prüfpfade im Projekt identisch aussehen.
/// </summary>
public static class LicenseVerifier
{
    /// <summary>Prüft gegen den eingebetteten, produktiven öffentlichen Schlüssel - der reguläre Aufrufer.</summary>
    public static bool Verify(LicenseFile license) =>
        Verify(license, LicensePublicKey.Bytes);

    /// <summary>
    /// Der Schlüssel ist ein Parameter (statt fest verdrahtet), damit Tests mit einem
    /// eigenen Wegwerf-Schlüsselpaar arbeiten können, ohne je den echten privaten
    /// Schlüssel zu benötigen (der nie im Repo liegt).
    /// </summary>
    public static bool Verify(LicenseFile license, byte[] publicKeyBytes)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(license.SignatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] hash;
        try
        {
            hash = SHA256.HashData(license.CanonicalPayload());
        }
        catch (Exception)
        {
            return false; // z. B. ein kaputtes ExpiresOnUtc, das sich nicht serialisieren lässt
        }

        try
        {
            var publicKey = new Ed25519PublicKeyParameters(publicKeyBytes, 0);
            var verifier = new Ed25519Signer();
            verifier.Init(false, publicKey);
            verifier.BlockUpdate(hash, 0, hash.Length);
            return verifier.VerifySignature(signature);
        }
        catch (Exception)
        {
            // z. B. publicKeyBytes mit falscher Länge - kein gültiger Schlüssel, keine gültige Prüfung.
            return false;
        }
    }
}
