using HaelpMi.Core.Models;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Bündelt eigene Identität + Geräteliste + Lizenz zur tatsächlichen "wer ist gerade
/// lizenzüberschritten"-Entscheidung (Issue #59/#60, siehe <see cref="LicenseLimitEvaluator"/>
/// für die reine Regel). Verwendet sowohl im Agent (Sende-/Empfangs-Sperre,
/// AlarmFlowCoordinator) als auch im Admin-Dashboard (Banner "nicht lizenziertes Gerät").
/// Fehlt eine gültig signierte Lizenz ganz (Missing/Invalid) gilt kein Limit - dieselbe
/// "Soft-Expiry, kein Hard-Lock"-Haltung wie beim bestehenden Ablauf-Hinweis (CLAUDE.md):
/// diese Funktion sperrt nur, wenn eine echte Lizenz ein konkretes Kontingent vorgibt und
/// das überschritten ist, nie als Nebenwirkung einer fehlenden/kaputten Lizenzdatei.
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
        return !LicenseLimitEvaluator.IsWithinLimit(identity.DeviceId, BuildKnownDevices(identity), _licenseProvider().License?.UserLimit);
    }

    public IReadOnlySet<Guid> GetDisabledDeviceIds()
    {
        var identity = _identityProvider();
        return LicenseLimitEvaluator.GetDisabledDeviceIds(BuildKnownDevices(identity), _licenseProvider().License?.UserLimit);
    }

    private List<LicenseLimitEvaluator.DeviceSeen> BuildKnownDevices(LiveIdentity identity)
    {
        var devices = _devicesProvider();
        // Eigenes Gerät trägt nie einen LicenseOverride (LiveIdentity hat kein lokales
        // DeviceEntry-Pendant, siehe Klassenkommentar bei AdminDashboardContext.LoadOwnDevice)
        // - vorbereiteter Erweiterungspunkt für Issue #61 gilt bislang nur für Peers.
        var known = new List<LicenseLimitEvaluator.DeviceSeen>(devices.Count + 1)
        {
            new(identity.DeviceId, identity.FirstSeenUtc),
        };
        known.AddRange(devices.Select(d => new LicenseLimitEvaluator.DeviceSeen(d.DeviceId, d.FirstSeenUtc, d.LicenseOverride)));
        return known;
    }
}
