using System;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace HaelpMi.InstallCreator.Licensing;

/// <summary>
/// Signiert Lizenzen mit dem Kunden-Lizenzschlüssel (Ed25519, CLAUDE.md "Lizenz &amp;
/// Secrets" Schlüssel Nr. 1 - separat vom Update-Signaturschlüssel). Signiert direkt über
/// <see cref="License.GetSigningPayload"/> ohne SHA-256-Vorstufe - muss exakt zu
/// <c>HaelpMi.Core.Licensing.LicenseReader.HasValidSignature</c> (Issue #19) passen, sonst
/// verifiziert der Kunde eine hier erstellte Lizenz nicht. BouncyCastle statt Eigenbau:
/// bereits Projektabhängigkeit für exakt dieses Verfahren (siehe HaelpMi.UpdateSigner).
///
/// Bewusst ohne Vaultwarden-Anbindung für den privaten Schlüssel (wie CustomerRegistryStore):
/// der Schlüssel wird pro Lauf per Datei geladen (<see cref="LoadPrivateKey"/>), nie auf der
/// Platte persistiert. Eine zentrale Ablage ist bei Bedarf ein eigenes Ticket, analog #42.
/// </summary>
internal static class LicenseFileSigner
{
    public static License CreateSigned(License unsigned, byte[] privateKeyBytes)
    {
        var payload = unsigned.GetSigningPayload();

        var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes, 0);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        var signature = signer.GenerateSignature();

        return unsigned with { SignatureBase64 = Convert.ToBase64String(signature) };
    }

    /// <summary>Für Tests/Sanity-Checks in dieser Codebasis - die echte Prüfung beim Kunden übernimmt #19.</summary>
    public static bool Verify(License signed, byte[] publicKeyBytes)
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

        var payload = signed.GetSigningPayload();
        var publicKey = new Ed25519PublicKeyParameters(publicKeyBytes, 0);
        var verifier = new Ed25519Signer();
        verifier.Init(false, publicKey);
        verifier.BlockUpdate(payload, 0, payload.Length);
        return verifier.VerifySignature(signature);
    }

    /// <summary>
    /// Liest den Base64-kodierten privaten Schlüssel aus einer Textdatei - gleiches Format wie
    /// <c>HaelpMi.UpdateSigner genkey</c> erzeugt (dieses generische Werkzeug kann auch für den
    /// separaten Lizenzschlüssel verwendet werden, es kennt keinen schlüsselspezifischen Zweck).
    /// </summary>
    public static byte[] LoadPrivateKey(string path) =>
        Convert.FromBase64String(System.IO.File.ReadAllText(path).Trim());
}
