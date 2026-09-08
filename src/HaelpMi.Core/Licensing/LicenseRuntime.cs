namespace HaelpMi.Core.Licensing;

/// <summary>
/// Issue #94: bündelt "einen fälligen Paketwechsel anwenden, falls vorhanden" (siehe
/// <see cref="PendingLicenseSwitch"/>) mit dem eigentlichen Lesen des aktiven Lizenzstands
/// - jede Stelle, die <see cref="LicenseReader.Load(Guid, byte[])"/> für den LIVE-Stand
/// aufruft (nicht die reinen Test-/Peer-Adoptionspfade), ruft stattdessen diese Methode auf.
/// </summary>
public static class LicenseRuntime
{
    public static LicenseCheckResult LoadCurrent(Guid ownCustomerGroupId, byte[] publicKeyBytes)
    {
        PendingLicenseSwitch.ApplyIfDue(ownCustomerGroupId, publicKeyBytes);
        return LicenseReader.Load(ownCustomerGroupId, publicKeyBytes);
    }
}
