namespace HaelpMi.Core.Models;

/// <summary>
/// Manuelles Deaktivieren/Freigeben eines Geräts im Geräte-Tab des Admin-Dashboards
/// (Issue #61) - siehe <see cref="DeviceEntry.LicenseOverride"/> und
/// <see cref="Licensing.LicenseLimitEvaluator"/> für die Auswertung, <see cref="DeviceEntry.LicenseOverrideSetAtUtc"/>
/// für die Gossip-Verbreitung an andere Geräte.
/// </summary>
public enum LicenseOverride
{
    /// <summary>Kein Override - normale FirstSeenUtc-Rangfolge entscheidet (Issue #59/#60).</summary>
    None,

    /// <summary>Immer aktiv, unabhängig von der Rangfolge - belegt trotzdem einen Kontingent-Platz.</summary>
    ForceEnabled,

    /// <summary>Immer deaktiviert und zählt nicht zum Kontingent - gibt einen Platz für andere Geräte frei.</summary>
    ForceDisabled,
}
