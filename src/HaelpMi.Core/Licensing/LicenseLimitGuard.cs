using HaelpMi.Core.Models;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Bündelt eigene Identität + Geräteliste + Lizenz zur tatsächlichen "wer ist gerade
/// lizenzüberschritten"-Entscheidung (Issue #59/#60, siehe <see cref="LicenseLimitEvaluator"/>
/// für die reine Regel). Verwendet sowohl im Agent (Sende-/Empfangs-Sperre,
/// AlarmFlowCoordinator) als auch im Admin-Dashboard (Banner "nicht lizenziertes Gerät").
///
/// Zwei Regeln, die anders als der Rest dieser Klasse NICHT aus der reinen
/// FirstSeenUtc-Rangfolge folgen, sondern hier bewusst vorgeschaltet sind (Nutzerkorrektur
/// 07.09.2026 nach einem Testaufbau mit 3 unlizenzierten Clients, die trotz Custom-2-Lizenz
/// alle funktionierten):
/// - <see cref="Role.Admin"/>-Geräte (Dashboard) zählen nie zum Kontingent und werden nie
///   deaktiviert - eine Lizenz kann sonst nie repariert werden, wenn ausgerechnet das
///   Dashboard selbst gesperrt wäre. "Nutzer"-Kontingent (z. B. "Custom: 2 Nutzer") meint
///   also ausschließlich User-Rolle-Geräte, nie den Admin-Sitz.
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
        if (identity.Role == Role.Admin)
        {
            return false;
        }

        return !LicenseLimitEvaluator.IsWithinLimit(identity.DeviceId, BuildKnownDevices(identity), EffectiveUserLimit());
    }

    public IReadOnlySet<Guid> GetDisabledDeviceIds()
    {
        var identity = _identityProvider();
        return LicenseLimitEvaluator.GetDisabledDeviceIds(BuildKnownDevices(identity), EffectiveUserLimit());
    }

    private int? EffectiveUserLimit()
    {
        var result = _licenseProvider();
        return result.Status is LicenseStatus.Missing or LicenseStatus.Invalid ? 0 : result.License?.UserLimit;
    }

    /// <summary>Nur User-Rolle-Geräte zählen zum Kontingent - Admin-Geräte werden erst gar nicht in den Kandidatenpool aufgenommen (siehe Klassenkommentar).</summary>
    private List<LicenseLimitEvaluator.DeviceSeen> BuildKnownDevices(LiveIdentity identity)
    {
        var devices = _devicesProvider();
        var known = new List<LicenseLimitEvaluator.DeviceSeen>(devices.Count + 1);
        if (identity.Role != Role.Admin)
        {
            known.Add(new(identity.DeviceId, identity.FirstSeenUtc));
        }

        known.AddRange(devices
            .Where(d => d.Role != Role.Admin)
            .Select(d => new LicenseLimitEvaluator.DeviceSeen(d.DeviceId, d.FirstSeenUtc, d.LicenseOverride)));
        return known;
    }
}
