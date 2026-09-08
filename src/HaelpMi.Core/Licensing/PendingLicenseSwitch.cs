using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Wendet eine per <see cref="LicenseImporter.ImportFromKeyText"/> vorgemerkte
/// Downgrade-Lizenz an, sobald sie fällig ist (Issue #94: "Die vorhandene Lizenz wird
/// regulär bis {Datum} genutzt und wechselt im Anschluss automatisch"). Fällig heißt: die
/// bisherige aktive Lizenz ist entweder gar nicht mehr gültig lesbar (Missing/Invalid -
/// dieselbe Sofort-Regel wie beim Einspielen selbst, siehe LicenseImporter) oder ihr
/// eigenes <see cref="License.ExpiryDateUtc"/> ist erreicht.
///
/// Kein eigener Timer (CLAUDE.md "kein Heartbeat, kein Polling im Leerlauf"): wird
/// stattdessen von <see cref="LicenseRuntime.LoadCurrent"/> vor jedem ohnehin
/// stattfindenden Lesen des Lizenzstands aufgerufen (App-Start, jeder Boot-Call-Refresh,
/// jedes Öffnen des Admin-Dashboards) - rein lokale Datei-I/O, kein Netzwerkverkehr.
/// </summary>
public static class PendingLicenseSwitch
{
    public static bool ApplyIfDue(Guid ownCustomerGroupId, byte[] publicKeyBytes) =>
        ApplyIfDue(AppPaths.LicenseFilePath, AppPaths.PendingLicenseFilePath, ownCustomerGroupId, publicKeyBytes, DateTime.UtcNow);

    internal static bool ApplyIfDue(string licenseFilePath, string pendingFilePath, Guid ownCustomerGroupId, byte[] publicKeyBytes, DateTime nowUtc)
    {
        var pendingCheck = LicenseReader.Load(pendingFilePath, ownCustomerGroupId, publicKeyBytes);
        if (pendingCheck.License is null)
        {
            return false;
        }

        var activeCheck = LicenseReader.Load(licenseFilePath, ownCustomerGroupId, publicKeyBytes);
        var isDue = activeCheck.License is null || nowUtc >= activeCheck.License.ExpiryDateUtc;
        if (!isDue)
        {
            return false;
        }

        JsonFileStore.Save(licenseFilePath, pendingCheck.License);
        if (File.Exists(pendingFilePath))
        {
            File.Delete(pendingFilePath);
        }

        return true;
    }
}
