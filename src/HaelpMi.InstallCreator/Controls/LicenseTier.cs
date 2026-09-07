namespace HaelpMi.InstallCreator.Controls;

/// <summary>
/// Lizenz-Paketgrößen. Enum-Namen wie im Freeze zu Issue #19 festgelegt - der Signierer
/// (<see cref="HaelpMi.Core.Licensing.License.GetSigningPayload"/>) verwendet
/// <c>Tier.ToString()</c> als Teil der signierten Bytes, <c>Trial</c> bleibt daher als
/// Symbol unverändert. Anzeigename ist davon getrennt: seit Einführung eines offiziellen
/// XS-Pakets (gleiches Nutzerlimit wie Trial) zeigt <see cref="LicenseTierLimits.GetDisplayLabel"/>
/// diese eine Stufe als "XS/Trial" statt nur "Trial". <c>Custom</c> ist für Testlizenzen mit
/// frei wählbarer Geräteanzahl (z. B. 2 Geräte für einen VM-Testaufbau) - Name muss mit dem
/// gleichnamigen Wert in <see cref="HaelpMi.Core.Licensing.LicenseTier"/> übereinstimmen, siehe
/// Duplikat-Begründung in <c>Licensing/License.cs</c>.
/// </summary>
public enum LicenseTier
{
    Trial,
    S,
    M,
    L,
    XL,
    Custom
}

public static class LicenseTierLimits
{
    /// <summary>
    /// Nutzerlimit je Paketgröße. <c>null</c> = unbegrenzt (nur bei XL). Für
    /// <see cref="LicenseTier.Custom"/> gibt es keine Staffel - der Wert kommt aus der
    /// Eingabe im <see cref="LicenseTierPicker"/>, nicht aus dieser Zuordnung.
    /// </summary>
    public static int? GetUserLimit(LicenseTier tier) => tier switch
    {
        LicenseTier.Trial => 10,
        LicenseTier.S => 25,
        LicenseTier.M => 75,
        LicenseTier.L => 150,
        LicenseTier.XL => null,
        LicenseTier.Custom => throw new InvalidOperationException(
            "Custom hat kein Staffel-Limit - Wert kommt aus der Nutzereingabe im LicenseTierPicker."),
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
    };

    private static string GetDisplayName(LicenseTier tier) =>
        tier == LicenseTier.Trial ? "XS/Trial" : tier.ToString();

    public static string GetDisplayLabel(LicenseTier tier)
    {
        if (tier == LicenseTier.Custom)
        {
            return "Custom - eigene Geräteanzahl";
        }

        var limit = GetUserLimit(tier);
        var name = GetDisplayName(tier);
        return limit is null ? $"{name} - unbegrenzt" : $"{name} - {limit} Nutzer";
    }
}

public sealed record LicenseTierOption(LicenseTier Tier, string Label);
