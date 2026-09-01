namespace HaelpMi.Core.Licensing;

/// <summary>
/// Anzeige-Stufe für den Banner aus Issue #20 - eine Verfeinerung von
/// <see cref="LicenseStatus"/>: <see cref="ExpiringSoon"/> existiert dort nicht, weil #19
/// nur den reinen Signatur-/Ablaufzustand kennt, nicht "steht demnächst an".
/// </summary>
public enum LicenseWarningLevel
{
    /// <summary>Lizenz gültig, kein bevorstehender Ablauf innerhalb des Warnfensters.</summary>
    None,

    /// <summary>Lizenz noch gültig, Ablaufdatum aber innerhalb von <see cref="LicenseWarningEvaluator.WarningWindowDays"/>.</summary>
    ExpiringSoon,

    Expired,
    Invalid,
    Missing,
}
