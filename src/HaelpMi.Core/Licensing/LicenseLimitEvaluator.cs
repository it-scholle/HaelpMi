namespace HaelpMi.Core.Licensing;

/// <summary>
/// Entscheidet dezentral, ohne zentrale Instanz (Issue #59/#60), welche Geräte innerhalb
/// des durch <see cref="License.UserLimit"/> vorgegebenen Lizenzkontingents liegen: alle
/// bekannten Geräte werden nach <see cref="DeviceSeen.FirstSeenUtc"/> aufsteigend sortiert
/// (bei exaktem Gleichstand zusätzlich nach <see cref="DeviceSeen.DeviceId"/>, rein zur
/// Stabilität) - die ersten <see cref="License.UserLimit"/> davon zählen als lizenziert, der
/// Rest als lizenzüberschritten. Das trifft bei einer Überschreitung stets die zuletzt
/// hinzugekommenen, neu ankündigenden Geräte (Nutzerentscheidung 07.09.2026), nie ein
/// länger laufendes Bestandsgerät. Jedes Gerät kommt bei gleichem Wissensstand (dieselben
/// DeviceIds/FirstSeenUtc-Werte) zwangsläufig zum selben Ergebnis wie jedes andere - eine
/// zentrale Freigabe- oder Sperrinstanz ist dafür nicht nötig.
/// </summary>
public static class LicenseLimitEvaluator
{
    public readonly record struct DeviceSeen(Guid DeviceId, DateTimeOffset FirstSeenUtc);

    /// <summary>Null bei <see cref="License.UserLimit"/> (XL/unbegrenzt) heißt: niemand ist je lizenzüberschritten.</summary>
    public static bool IsWithinLimit(Guid deviceId, IReadOnlyCollection<DeviceSeen> knownDevices, int? userLimit)
    {
        if (userLimit is null)
        {
            return true;
        }

        var rank = OrderedDeviceIds(knownDevices).IndexOf(deviceId);
        return rank >= 0 && rank < userLimit.Value;
    }

    /// <summary>Alle DeviceIds, die laut derselben Regel wie <see cref="IsWithinLimit"/> gerade lizenzüberschritten sind.</summary>
    public static IReadOnlySet<Guid> GetDisabledDeviceIds(IReadOnlyCollection<DeviceSeen> knownDevices, int? userLimit)
    {
        if (userLimit is null)
        {
            return new HashSet<Guid>();
        }

        return OrderedDeviceIds(knownDevices).Skip(userLimit.Value).ToHashSet();
    }

    private static List<Guid> OrderedDeviceIds(IReadOnlyCollection<DeviceSeen> knownDevices) =>
        knownDevices
            .OrderBy(d => d.FirstSeenUtc)
            .ThenBy(d => d.DeviceId)
            .Select(d => d.DeviceId)
            .ToList();
}
