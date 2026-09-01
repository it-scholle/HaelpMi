using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// "Lizenz einspielen" (Issue #20-Banner, siehe auch Issue #51 - Lizenz-Import im
/// Admin-Dashboard): kopiert eine vom Admin ausgewählte Lizenzdatei erst NACH erfolgreicher
/// #19-Prüfung an den festen Lizenzpfad - eine ungültige/manipulierte/fremde Datei
/// überschreibt eine bestehende gültige Lizenz nie. Baut bewusst auf
/// <see cref="LicenseReader"/> auf statt die Prüfung zu duplizieren.
/// </summary>
public static class LicenseImporter
{
    public static LicenseImportResult Import(string sourceFilePath, Guid ownCustomerGroupId) =>
        Import(sourceFilePath, ownCustomerGroupId, AppPaths.LicenseFilePath, LicensePublicKey.Bytes);

    internal static LicenseImportResult Import(string sourceFilePath, Guid ownCustomerGroupId, string destinationFilePath, byte[] publicKeyBytes)
    {
        var checkResult = LicenseReader.Load(sourceFilePath, ownCustomerGroupId, publicKeyBytes);
        if (checkResult.Status is LicenseStatus.Invalid or LicenseStatus.Missing)
        {
            return new LicenseImportResult(false, checkResult);
        }

        // Soft-Expiry (CLAUDE.md): eine korrekt signierte, aber bereits abgelaufene Lizenz
        // wird trotzdem übernommen - genau wie eine ganz normal eingelesene (#19), nicht
        // strenger nur weil sie gerade importiert statt mitgeliefert wurde.
        JsonFileStore.Save(destinationFilePath, checkResult.License!);
        return new LicenseImportResult(true, checkResult);
    }
}

public sealed record LicenseImportResult(bool Success, LicenseCheckResult CheckResult);
