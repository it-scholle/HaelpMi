using System.Text.Json;
using HaelpMi.Core.Storage;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Liest die mitgelieferte Lizenzdatei (<see cref="AppPaths.LicenseFilePath"/>) und prüft
/// sie gegen die eigene <see cref="License.CustomerGroupId"/> - eine gültig signierte
/// Lizenz einer anderen Kundengruppe wird genauso als <see cref="LicenseStatus.Invalid"/>
/// abgelehnt wie eine manipulierte Datei (#18-Diskussion zu #31: ein reines Ablaufdatum
/// ließe sich sonst einfach bei einer anderen Installation einspielen). Kein Hard-Lock bei
/// Ablauf oder Fehlern (CLAUDE.md "Lizenz &amp; Secrets") - jedes Ergebnis außer
/// <see cref="LicenseStatus.Valid"/> ist für den Aufrufer ein reiner Informationsstand,
/// kein Abbruchgrund.
/// </summary>
public static class LicenseReader
{
    public static LicenseCheckResult Load(Guid ownCustomerGroupId) =>
        Load(AppPaths.LicenseFilePath, ownCustomerGroupId, LicensePublicKey.Bytes);

    /// <summary>
    /// Dateipfad und Schlüssel als Parameter (statt fest verdrahtet), damit Tests mit einer
    /// temporären Datei und einem eigenen Wegwerf-Schlüsselpaar arbeiten können, ohne je den
    /// echten privaten Schlüssel zu benötigen - gleiches Prinzip wie
    /// <c>UpdatePackageVerifier.Verify</c>.
    /// </summary>
    internal static LicenseCheckResult Load(string filePath, Guid ownCustomerGroupId, byte[] publicKeyBytes)
    {
        if (!File.Exists(filePath))
        {
            return new LicenseCheckResult(LicenseStatus.Missing, null);
        }

        License? license;
        try
        {
            license = JsonSerializer.Deserialize<License>(File.ReadAllText(filePath));
        }
        catch (JsonException)
        {
            return new LicenseCheckResult(LicenseStatus.Invalid, null);
        }
        catch (IOException)
        {
            // Datei kurzzeitig nicht lesbar (z. B. AV-Scan) - wie eine fehlende Datei
            // behandeln statt fälschlich als manipuliert einzustufen.
            return new LicenseCheckResult(LicenseStatus.Missing, null);
        }

        if (license is null || license.CustomerGroupId != ownCustomerGroupId || !HasValidSignature(license, publicKeyBytes))
        {
            return new LicenseCheckResult(LicenseStatus.Invalid, null);
        }

        var status = license.ExpiryDateUtc < DateTime.UtcNow ? LicenseStatus.Expired : LicenseStatus.Valid;
        return new LicenseCheckResult(status, license);
    }

    private static bool HasValidSignature(License license, byte[] publicKeyBytes)
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

        var payload = license.GetSigningPayload();
        var publicKey = new Ed25519PublicKeyParameters(publicKeyBytes, 0);
        var verifier = new Ed25519Signer();
        verifier.Init(false, publicKey);
        verifier.BlockUpdate(payload, 0, payload.Length);
        return verifier.VerifySignature(signature);
    }
}
