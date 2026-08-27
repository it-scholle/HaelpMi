using System;
using System.Globalization;
using System.Text;
using HaelpMi.InstallCreator.Controls;

namespace HaelpMi.InstallCreator.Licensing;

/// <summary>
/// Bewusstes Duplikat von <c>HaelpMi.Core.Licensing.License</c> - HaelpMi.InstallCreator hat
/// keine ProjectReference auf HaelpMi.Core (siehe HaelpMi.InstallCreator.csproj), teilt also
/// keine Laufzeit-Modelle. <see cref="GetSigningPayload"/> muss deshalb Feld für Feld und
/// Byte für Byte mit der dortigen Fassung übereinstimmen (Issue #19) - sonst verifiziert der
/// Kunde eine hier erstellte Lizenz nicht.
/// </summary>
internal sealed record License(
    Guid CustomerGroupId,
    LicenseTier Tier,
    int? UserLimit,
    DateTime IssuedAtUtc,
    DateTime ExpiryDateUtc,
    string SignatureBase64)
{
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

    private static string FormatUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
}
