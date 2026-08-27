namespace HaelpMi.InstallCreator.Licensing;

/// <summary>
/// Lizenz-Staffelung (Nutzer-Abstimmung 27.08.2026, siehe Issue #18/#46) - es gibt keine
/// "Default-Lizenz" mehr, jede Lizenz bekommt genau einen dieser Tiers. Werte identisch zur
/// Kundengruppen-Größe (Wartungsvertrag), nicht zur Geräteanzahl.
/// </summary>
internal enum LicenseTier
{
    Trial,
    S,
    M,
    L,
    XL,
}

internal static class LicenseTierExtensions
{
    /// <summary>Nutzerlimit des Tiers, <c>null</c> = unbegrenzt (nur XL, 151+).</summary>
    public static int? UserLimit(this LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 10,
        LicenseTier.S => 25,
        LicenseTier.M => 75,
        LicenseTier.L => 150,
        LicenseTier.XL => null,
        _ => throw new System.ArgumentOutOfRangeException(nameof(tier)),
    };
}
