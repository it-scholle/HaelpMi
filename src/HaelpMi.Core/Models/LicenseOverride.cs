namespace HaelpMi.Core.Models;

/// <summary>
/// Vorbereiteter Erweiterungspunkt für Issue #61 (manuelles Deaktivieren/Freigeben eines
/// Geräts im Admin-Dashboard, Meilenstein release-2.0) - siehe
/// <see cref="DeviceEntry.LicenseOverride"/> und <see cref="Licensing.LicenseLimitEvaluator"/>.
/// Heute noch ohne jede UI, Netzwerk-Propagierung oder Signaturprüfung angebunden (bleibt
/// #61 vorbehalten, braucht die noch fehlende Admin-Rollen-Signatur-Infrastruktur) - nur der
/// Rechenkern kennt diesen Wert bereits, damit #61 später nicht mehr die bereits gemergte
/// Kernlogik aus Issue #59/#60 anfassen muss.
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
