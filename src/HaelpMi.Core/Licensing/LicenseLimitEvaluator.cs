using HaelpMi.Core.Models;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Entscheidet dezentral, ohne zentrale Instanz (Issue #59/#60), welche Geräte innerhalb
/// des durch <see cref="License.UserLimit"/> vorgegebenen Lizenzkontingents liegen: alle
/// bekannten Geräte ohne <see cref="LicenseOverride"/> werden nach
/// <see cref="DeviceSeen.FirstSeenUtc"/> aufsteigend sortiert (bei exaktem Gleichstand
/// zusätzlich nach <see cref="DeviceSeen.DeviceId"/>, rein zur Stabilität) - die ersten
/// <see cref="License.UserLimit"/> davon zählen als lizenziert, der Rest als
/// lizenzüberschritten. Das trifft bei einer Überschreitung stets die zuletzt
/// hinzugekommenen, neu ankündigenden Geräte (Nutzerentscheidung 07.09.2026), nie ein
/// länger laufendes Bestandsgerät. Jedes Gerät kommt bei gleichem Wissensstand (dieselben
/// DeviceIds/FirstSeenUtc/Override-Werte) zwangsläufig zum selben Ergebnis wie jedes
/// andere - eine zentrale Freigabe- oder Sperrinstanz ist dafür nicht nötig.
///
/// <see cref="DeviceSeen.Override"/> (vorbereiteter Erweiterungspunkt für Issue #61, siehe
/// <see cref="DeviceEntry.LicenseOverride"/>): ein <see cref="LicenseOverride.ForceDisabled"/>-
/// Gerät ist immer deaktiviert und belegt keinen Kontingent-Platz (gibt einen Platz für
/// andere frei); ein <see cref="LicenseOverride.ForceEnabled"/>-Gerät ist immer aktiv, belegt
/// aber weiterhin einen Platz (verkleinert das für die Rangfolge verbleibende Kontingent) -
/// ein manueller Override darf das Lizenzkontingent selbst nicht aushebeln. Heute setzt
/// nichts diesen Wert je auf etwas anderes als <see cref="LicenseOverride.None"/>.
/// </summary>
public static class LicenseLimitEvaluator
{
    public readonly record struct DeviceSeen(Guid DeviceId, DateTimeOffset FirstSeenUtc, LicenseOverride Override = LicenseOverride.None);

    /// <summary>Null bei <see cref="License.UserLimit"/> (XL/unbegrenzt) heißt: niemand ist je lizenzüberschritten (ForceDisabled bleibt trotzdem deaktiviert).</summary>
    public static bool IsWithinLimit(Guid deviceId, IReadOnlyCollection<DeviceSeen> knownDevices, int? userLimit)
    {
        var ownOverride = knownDevices.FirstOrDefault(d => d.DeviceId == deviceId).Override;
        switch (ownOverride)
        {
            case LicenseOverride.ForceEnabled:
                return true;
            case LicenseOverride.ForceDisabled:
                return false;
        }

        if (userLimit is null)
        {
            return true;
        }

        var rank = OrderedDeviceIds(RankedPool(knownDevices)).IndexOf(deviceId);
        return rank >= 0 && rank < EffectiveLimit(knownDevices, userLimit.Value);
    }

    /// <summary>Alle DeviceIds, die laut derselben Regel wie <see cref="IsWithinLimit"/> gerade lizenzüberschritten sind (inkl. ForceDisabled, auch bei unbegrenzter Lizenz).</summary>
    public static IReadOnlySet<Guid> GetDisabledDeviceIds(IReadOnlyCollection<DeviceSeen> knownDevices, int? userLimit)
    {
        var forceDisabled = knownDevices.Where(d => d.Override == LicenseOverride.ForceDisabled).Select(d => d.DeviceId);
        if (userLimit is null)
        {
            return forceDisabled.ToHashSet();
        }

        var disabled = OrderedDeviceIds(RankedPool(knownDevices)).Skip(EffectiveLimit(knownDevices, userLimit.Value)).ToHashSet();
        disabled.UnionWith(forceDisabled);
        return disabled;
    }

    /// <summary>Geräte ohne Override - nur diese durchlaufen die FirstSeenUtc-Rangfolge.</summary>
    private static IEnumerable<DeviceSeen> RankedPool(IReadOnlyCollection<DeviceSeen> knownDevices) =>
        knownDevices.Where(d => d.Override == LicenseOverride.None);

    /// <summary>Lizenzkontingent abzüglich der bereits per ForceEnabled fest belegten Plätze, nie negativ.</summary>
    private static int EffectiveLimit(IReadOnlyCollection<DeviceSeen> knownDevices, int userLimit) =>
        Math.Max(0, userLimit - knownDevices.Count(d => d.Override == LicenseOverride.ForceEnabled));

    private static List<Guid> OrderedDeviceIds(IEnumerable<DeviceSeen> knownDevices) =>
        knownDevices
            .OrderBy(d => d.FirstSeenUtc)
            .ThenBy(d => d.DeviceId)
            .Select(d => d.DeviceId)
            .ToList();
}
