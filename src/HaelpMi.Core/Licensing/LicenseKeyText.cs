using System.Globalization;
using System.Text;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Kompakte Text-Kodierung einer <see cref="License"/> für Übermittlung als Klartext (z. B.
/// per E-Mail) statt als Datei-Anhang - Nutzerentscheidung 01.09.2026, ersetzt den
/// ursprünglichen Plan aus Issue #54 (Base64-Envelope um eine .json). Format an ein
/// JWT-Token angelehnt: "HAELPMI1.&lt;Base64Url(Payload)&gt;.&lt;Base64Url(Signatur)&gt;" -
/// "." als Trenner, weil er im Base64Url-Alphabet nicht vorkommt (sonst beim Zerlegen
/// mehrdeutig). Der kodierte Payload ist exakt <see cref="License.GetSigningPayload"/>,
/// keine zweite Signaturgrundlage - Fälschungssicherheit unverändert wie beim bisherigen
/// Dateiformat (siehe LicensingTests, insbesondere die Manipulations-Fälle).
///
/// Bewusst nur Formatzerlegung hier, keine Signaturprüfung - die bleibt einzig bei
/// <see cref="LicenseReader"/>, damit es nur eine tatsächliche Prüfstelle gibt.
/// </summary>
public static class LicenseKeyText
{
    private const string Prefix = "HAELPMI1";

    public static string Encode(License license)
    {
        var payloadB64 = Base64UrlEncode(license.GetSigningPayload());
        var signatureB64 = Base64UrlEncode(Convert.FromBase64String(license.SignatureBase64));
        return $"{Prefix}.{payloadB64}.{signatureB64}";
    }

    /// <summary>Reine Formatzerlegung in die noch ungeprüften Rohbytes - für die Signaturprüfung in <see cref="LicenseReader"/>.</summary>
    internal static bool TryDecode(string keyText, out byte[] payloadBytes, out byte[] signatureBytes)
    {
        payloadBytes = [];
        signatureBytes = [];
        var parts = keyText.Trim().Split('.');
        if (parts.Length != 3 || parts[0] != Prefix)
        {
            return false;
        }

        try
        {
            payloadBytes = Base64UrlDecode(parts[1]);
            signatureBytes = Base64UrlDecode(parts[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Zerlegt bereits als authentisch geprüfte Payload-Bytes in die fachlichen Felder -
    /// erst NACH erfolgreicher Signaturprüfung aufrufen (siehe LicenseReader.LoadFromKeyText).
    /// </summary>
    internal static License? ParsePayload(byte[] payloadBytes, string signatureBase64)
    {
        var fields = Encoding.UTF8.GetString(payloadBytes).Split('|');
        if (fields.Length != 5 || !Guid.TryParseExact(fields[0], "D", out var customerGroupId) || !Enum.TryParse<LicenseTier>(fields[1], out var tier))
        {
            return null;
        }

        int? userLimit;
        if (fields[2].Length == 0)
        {
            userLimit = null;
        }
        else if (int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit))
        {
            userLimit = parsedLimit;
        }
        else
        {
            return null;
        }

        if (!TryParseRoundTripUtc(fields[3], out var issuedAtUtc) || !TryParseRoundTripUtc(fields[4], out var expiryDateUtc))
        {
            return null;
        }

        return new License(customerGroupId, tier, userLimit, issuedAtUtc, expiryDateUtc, signatureBase64);
    }

    // Muss exakt zum Format in License.GetSigningPayload (FormatUtc) passen, sonst würde ein
    // korrekt signierter Text hier als unlesbar statt als gültig erkannt.
    private static bool TryParseRoundTripUtc(string text, out DateTime value) =>
        DateTime.TryParseExact(text, "yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        var missingPadding = (4 - padded.Length % 4) % 4;
        return Convert.FromBase64String(padded + new string('=', missingPadding));
    }
}
