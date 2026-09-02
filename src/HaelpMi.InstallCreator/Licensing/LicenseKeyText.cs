namespace HaelpMi.InstallCreator.Licensing;

/// <summary>
/// Bewusstes Duplikat von HaelpMi.Core.Licensing.LicenseKeyText.Encode (keine
/// ProjectReference, siehe HaelpMi.InstallCreator.csproj) - InstallCreator braucht nur die
/// Encode-Richtung, nie Decode/Prüfung (das macht ausschließlich der Kunde/LicenseReader).
/// Muss Byte für Byte zum Core-Gegenstück passen, siehe dortigen Formatkommentar.
/// </summary>
internal static class LicenseKeyText
{
    private const string Prefix = "HAELPMI1";

    public static string Encode(License license)
    {
        var payloadB64 = Base64UrlEncode(license.GetSigningPayload());
        var signatureB64 = Base64UrlEncode(Convert.FromBase64String(license.SignatureBase64));
        return $"{Prefix}.{payloadB64}.{signatureB64}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
