using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace HaelpMi.LicenseSigner;

/// <summary>
/// Reine Ed25519-Erzeugungs-/Signierlogik für Kunden-Lizenzdateien, Struktur-Klon von
/// HaelpMi.UpdateSigner/UpdateSigningOperations.cs. Der CLI-Einstiegspunkt (Program.cs)
/// ruft nur diese Methoden auf.
/// </summary>
public static class LicenseSigningOperations
{
    public readonly record struct KeyPair(byte[] PrivateKey, byte[] PublicKey);

    public static KeyPair GenerateKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();

        var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (Ed25519PublicKeyParameters)keyPair.Public;

        return new KeyPair(privateKey.GetEncoded(), publicKey.GetEncoded());
    }

    /// <summary>
    /// Signiert Kundenname/Sitzanzahl/Ablaufdatum (SHA-256 + Ed25519) und liefert die
    /// fertige Lizenzdatei zum Serialisieren.
    /// </summary>
    public static LicenseFileData Sign(string customerName, int seatCount, DateOnly expiresOnUtc, byte[] privateKeyBytes)
    {
        var hash = SHA256.HashData(CanonicalPayload(customerName, seatCount, expiresOnUtc));

        var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes, 0);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(hash, 0, hash.Length);
        var signature = signer.GenerateSignature();

        return new LicenseFileData(customerName, seatCount, expiresOnUtc, Convert.ToBase64String(signature));
    }

    /// <summary>
    /// MUSS byteidentisch zu HaelpMi.Core.Licensing.LicenseFile.CanonicalPayload() bleiben -
    /// exakt dieselbe Feldform, dieselben (Standard-)JsonSerializerOptions. Ein
    /// Auseinanderlaufen bricht jede Signaturprüfung.
    /// </summary>
    private static byte[] CanonicalPayload(string customerName, int seatCount, DateOnly expiresOnUtc) =>
        JsonSerializer.SerializeToUtf8Bytes(new { CustomerName = customerName, SeatCount = seatCount, ExpiresOnUtc = expiresOnUtc });

    public static string ToLicenseJson(LicenseFileData license) =>
        JsonSerializer.Serialize(license, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>Gleiche Feldform wie HaelpMi.Core.Licensing.LicenseFile - JSON-Format bleibt kompatibel.</summary>
public sealed record LicenseFileData(string CustomerName, int SeatCount, DateOnly ExpiresOnUtc, string SignatureBase64);
