using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace HaelpMi.InstallCreator.Licensing;

/// <summary>
/// Signiert/prüft Lizenzen mit dem Kunden-Lizenzschlüssel (Ed25519, CLAUDE.md "Lizenz &amp;
/// Secrets" Schlüssel Nr. 1 - separat vom Update-Signaturschlüssel). Gleiches Signaturschema
/// (SHA-256 über die kanonische Nutzlast, dann Ed25519 über den Hash) wie
/// <c>HaelpMi.Core.Updates.UpdatePackageVerifier</c>, damit #19 dieselbe Prüflogik übernehmen
/// kann. BouncyCastle statt Eigenbau: bereits Projektabhängigkeit für exakt dieses Verfahren
/// (siehe HaelpMi.UpdateSigner), kein neuer Abwägungsbedarf.
///
/// Bewusst ohne Vaultwarden-Anbindung für den privaten Schlüssel (wie CustomerRegistryStore):
/// der Schlüssel wird pro Lauf per Datei geladen (<see cref="LoadPrivateKey"/>), nie auf der
/// Platte persistiert. Eine zentrale Ablage ist bei Bedarf ein eigenes Ticket, analog #42.
/// </summary>
internal static class LicenseFileSigner
{
    public static SignedLicenseFile CreateSigned(LicenseFile license, byte[] privateKeyBytes)
    {
        var hash = HashOf(license.CustomerGroupId, license.Tier.ToString(), license.UserLimit, license.IssuedAtUtc, license.ExpiryDateUtc);

        var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes, 0);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(hash, 0, hash.Length);
        var signature = signer.GenerateSignature();

        return new SignedLicenseFile(
            license.CustomerGroupId, license.Tier.ToString(), license.UserLimit,
            license.IssuedAtUtc, license.ExpiryDateUtc, Convert.ToBase64String(signature));
    }

    public static bool Verify(SignedLicenseFile signed, byte[] publicKeyBytes)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signed.SignatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        var hash = HashOf(signed.CustomerGroupId, signed.Tier, signed.UserLimit, signed.IssuedAtUtc, signed.ExpiryDateUtc);
        var publicKey = new Ed25519PublicKeyParameters(publicKeyBytes, 0);
        var verifier = new Ed25519Signer();
        verifier.Init(false, publicKey);
        verifier.BlockUpdate(hash, 0, hash.Length);
        return verifier.VerifySignature(signature);
    }

    /// <summary>
    /// Liest den Base64-kodierten privaten Schlüssel aus einer Textdatei - gleiches Format wie
    /// <c>HaelpMi.UpdateSigner genkey</c> erzeugt (dieses generische Werkzeug kann auch für den
    /// separaten Lizenzschlüssel verwendet werden, es kennt keinen schlüsselspezifischen Zweck).
    /// </summary>
    public static byte[] LoadPrivateKey(string path) =>
        Convert.FromBase64String(System.IO.File.ReadAllText(path).Trim());

    private static byte[] HashOf(Guid customerGroupId, string tier, int? userLimit, DateTime issuedAtUtc, DateTime expiryDateUtc)
    {
        var canonicalJson = JsonSerializer.Serialize(new
        {
            CustomerGroupId = customerGroupId,
            Tier = tier,
            UserLimit = userLimit,
            IssuedAtUtc = issuedAtUtc,
            ExpiryDateUtc = expiryDateUtc,
        });
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
    }
}
