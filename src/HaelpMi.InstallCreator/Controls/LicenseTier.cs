namespace HaelpMi.InstallCreator.Controls;

/// <summary>
/// Lizenz-Paketgrößen. Enum-Namen wie im Freeze zu Issue #19 festgelegt - der Signierer
/// (<see cref="HaelpMi.Core.Licensing.License.GetSigningPayload"/>) verwendet
/// <c>Tier.ToString()</c> als Teil der signierten Bytes, <c>Trial</c> bleibt daher als
/// Symbol unverändert. Anzeigename ist davon getrennt: seit Einführung eines offiziellen
/// XS-Pakets (gleiches Nutzerlimit wie Trial) zeigt <see cref="LicenseTierLimits.GetDisplayLabel"/>
/// diese eine Stufe als "XS/Trial" statt nur "Trial".
/// </summary>
public enum LicenseTier
{
    Trial,
    S,
    M,
    L,
    XL
}

public static class LicenseTierLimits
{
    /// <summary>Nutzerlimit je Paketgröße. <c>null</c> = unbegrenzt (nur bei XL).</summary>
    public static int? GetUserLimit(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 10,
        LicenseTier.S => 25,
        LicenseTier.M => 75,
        LicenseTier.L => 150,
        LicenseTier.XL => null,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
    };

    private static string GetDisplayName(LicenseTier tier) =>
        tier == LicenseTier.Trial ? "XS/Trial" : tier.ToString();

    public static string GetDisplayLabel(LicenseTier tier)
    {
        var limit = GetUserLimit(tier);
        var name = GetDisplayName(tier);
        return limit is null ? $"{name} - unbegrenzt" : $"{name} - {limit} Nutzer";
    }
}

public sealed record LicenseTierOption(LicenseTier Tier, string Label);
