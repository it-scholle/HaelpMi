using HaelpMi.Core.Models;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Bündelt eigene Identität + Geräteliste + Lizenz zur tatsächlichen "wer ist gerade
/// lizenzüberschritten"-Entscheidung (Issue #59/#60, siehe <see cref="LicenseLimitEvaluator"/>
/// für die reine Regel). Verwendet sowohl im Agent (Sende-/Empfangs-Sperre,
/// AlarmFlowCoordinator) als auch im Admin-Dashboard (Banner "nicht lizenziertes Gerät").
///
/// Zwei Regeln, die anders als der Rest dieser Klasse NICHT aus der reinen
/// FirstSeenUtc-Rangfolge folgen, sondern hier bewusst vorgeschaltet sind:
/// - <see cref="Role.Admin"/>-Geräte (Dashboard) werden nie deaktiviert - eine Lizenz kann
///   sonst nie repariert werden, wenn ausgerechnet das Dashboard selbst gesperrt wäre. Sie
///   ZÄHLEN aber weiterhin zum Kontingent (Nutzerkorrektur 07.09.2026, nach einem
///   Testaufbau: "Admin + 2 User" passte bei einer Custom-2-Lizenz, sollte aber "Admin + 1
///   User" als Maximum sein) - technisch derselbe Mechanismus wie
///   <see cref="LicenseOverride.ForceEnabled"/> (siehe <see cref="LicenseLimitEvaluator"/>):
///   immer aktiv, belegt aber trotzdem einen Platz.
/// - Fehlt eine gültig signierte Lizenz ganz (<see cref="LicenseStatus.Missing"/>/
///   <see cref="LicenseStatus.Invalid"/>), gilt das Kontingent als 0 statt als unbegrenzt:
///   ein Gerät ohne jede erkennbare Lizenz darf nie "versehentlich frei laufen", nur weil
///   ihm (z. B. weil ein späterer Lizenz-Import nie dieses konkrete Gerät erreicht hat -
///   "Lizenz einspielen" im Dashboard aktualisiert bislang nur das dortige lokale Gerät,
///   siehe LicenseImporter) schlicht keine Lizenzdatei vorliegt. Eine bereits abgelaufene,
///   aber einst gültig ausgestellte Lizenz ist davon unberührt (Klassifizierung liefert das
///   reale UserLimit weiterhin, siehe LicenseReader.Classify) - "Soft-Expiry, kein
///   Hard-Lock" (CLAUDE.md) gilt unverändert für den Ablauf, nur nicht für eine komplett
///   fehlende/kaputte Lizenzdatei.
/// </summary>
public sealed class LicenseLimitGuard
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Func<LicenseCheckResult> _licenseProvider;
    private readonly Func<List<DeviceEntry>> _devicesProvider;

    public LicenseLimitGuard(Func<LiveIdentity> identityProvider, Func<LicenseCheckResult> licenseProvider, Func<List<DeviceEntry>> devicesProvider)
    {
        _identityProvider = identityProvider;
        _licenseProvider = licenseProvider;
        _devicesProvider = devicesProvider;
    }

    public bool IsOwnDeviceDisabled()
    {
        var identity = _identityProvider();
        return !LicenseLimitEvaluator.IsWithinLimit(identity.DeviceId, BuildKnownDevices(identity), EffectiveUserLimit());
    }

    public IReadOnlySet<Guid> GetDisabledDeviceIds()
    {
        var identity = _identityProvider();
        return LicenseLimitEvaluator.GetDisabledDeviceIds(BuildKnownDevices(identity), EffectiveUserLimit());
    }

    /// <summary>Issue #61 (Geräte-Tab "x/y lizenziert"): dieselbe Regel wie <see cref="GetDisabledDeviceIds"/>, hier nur als reiner Anzeigewert.</summary>
    public int? GetEffectiveUserLimit() => EffectiveUserLimit();

    private int? EffectiveUserLimit()
    {
        var result = _licenseProvider();
        return result.Status is LicenseStatus.Missing or LicenseStatus.Invalid ? 0 : result.License?.UserLimit;
    }

    /// <summary>Admin-Rolle-Geräte gehen als ForceEnabled in den Pool ein (siehe Klassenkommentar) - zählen mit, werden aber nie deaktiviert.</summary>
    private List<LicenseLimitEvaluator.DeviceSeen> BuildKnownDevices(LiveIdentity identity)
    {
        var devices = _devicesProvider();
        var known = new List<LicenseLimitEvaluator.DeviceSeen>(devices.Count + 1)
        {
            new(identity.DeviceId, identity.FirstSeenUtc, EffectiveOverride(identity.Role, LicenseOverride.None)),
        };

        known.AddRange(devices.Select(d =>
            new LicenseLimitEvaluator.DeviceSeen(d.DeviceId, d.FirstSeenUtc, EffectiveOverride(d.Role, d.LicenseOverride))));
        return known;
    }

    private static LicenseOverride EffectiveOverride(Role role, LicenseOverride storedOverride) =>
        role == Role.Admin ? LicenseOverride.ForceEnabled : storedOverride;
}
