using System.Globalization;
using System.Text;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Eine an genau eine Kundengruppe gebundene Lizenz (Lizenz-Dateiformat v1, siehe
/// Issue #19-Kommentar "Freeze für parallele Bearbeitung"). Wird vom Install-Creator
/// erzeugt und signiert (Issue #18) und beim Kunden als eigenständige Datei neben
/// deployment.json ausgeliefert (<see cref="Storage.AppPaths.LicenseFilePath"/>). Ein
/// reines Ablaufdatum bindet nichts an die Installation (siehe #18-Diskussion zu #31) -
/// deshalb ist <see cref="CustomerGroupId"/> Teil der signierten Felder, nicht nur eine
/// Begleitinformation.
/// </summary>
public sealed record License(
    Guid CustomerGroupId,
    LicenseTier Tier,
    int? UserLimit,
    DateTime IssuedAtUtc,
    DateTime ExpiryDateUtc,
    string SignatureBase64)
{
    /// <summary>
    /// Byte-Repräsentation der signierten Felder (alles außer <see cref="SignatureBase64"/>
    /// selbst), pipe-getrennt in fester Reihenfolge - bewusst keine JSON-Serialisierung als
    /// Signaturgrundlage, damit die Prüfung nicht von Property-Reihenfolge oder
    /// Formatierungsdetails eines Serializers abhängt. Ein künftiger Signierer (#18) muss
    /// diese Reihenfolge exakt genauso bilden.
    /// </summary>
    public byte[] GetSigningPayload()
    {
        var text = string.Join('|',
            CustomerGroupId.ToString("D"),
            Tier.ToString(),
            UserLimit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            FormatUtc(IssuedAtUtc),
            FormatUtc(ExpiryDateUtc));
        return Encoding.UTF8.GetBytes(text);
    }

    // DateTime.Kind ist nach einer JSON-Deserialisierung nicht garantiert Utc (hängt vom
    // Format in der Datei ab) - SpecifyKind statt ToUniversalTime, damit die Signaturbasis
    // unabhängig vom eingelesenen Kind immer dieselben Bytes ergibt.
    private static string FormatUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
}
