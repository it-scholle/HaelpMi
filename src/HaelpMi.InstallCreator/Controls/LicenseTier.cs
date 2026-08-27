namespace HaelpMi.InstallCreator.Controls;

/// <summary>
/// Lizenz-Paketgrößen. Werte und Namen wie im Freeze zu Issue #19 festgelegt - müssen bei
/// künftiger Signier-/Prüf-Implementierung (Issues #18/#19) unverändert übernommen werden,
/// da sie 1:1 dem <c>Tier</c>-Feld der signierten Lizenzdatei entsprechen.
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

    public static string GetDisplayLabel(LicenseTier tier)
    {
        var limit = GetUserLimit(tier);
        return limit is null ? $"{tier} - unbegrenzt" : $"{tier} - {limit} Nutzer";
    }
}

public sealed record LicenseTierOption(LicenseTier Tier, string Label);
