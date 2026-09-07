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
    public static LicenseImportResult Import(string sourceFilePath, Guid ownCustomerGroupId, byte[] publicKeyBytes) =>
        Import(sourceFilePath, ownCustomerGroupId, AppPaths.LicenseFilePath, publicKeyBytes);

    internal static LicenseImportResult Import(string sourceFilePath, Guid ownCustomerGroupId, string destinationFilePath, byte[] publicKeyBytes)
    {
        var checkResult = LicenseReader.Load(sourceFilePath, ownCustomerGroupId, publicKeyBytes);
        return Persist(checkResult, destinationFilePath);
    }

    /// <summary>
    /// Issue #54-Nacharbeit: Lizenz als eingefügter Text statt Datei-Import (#51). Interne
    /// Ablage bleibt unverändert JSON an <see cref="AppPaths.LicenseFilePath"/> - nur der
    /// Übermittlungsweg zum Admin ändert sich, der bereits getestete Boot-Einlese-Pfad
    /// (<see cref="LicenseReader.Load(Guid)"/>) bleibt unberührt.
    ///
    /// Nutzervorgabe (02.09.2026, Fehlerbericht "Button macht nichts"): anders als der
    /// Datei-Import oben unterscheidet dieser Pfad die Fehlerart (nicht erkennbar/falscher
    /// Kunde/abgelaufen), damit der Admin eine konkrete Meldung statt nur "ungültig" sieht.
    /// Eine bereits abgelaufene, aber sonst korrekt signierte Lizenz wird beim MANUELLEN
    /// Import bewusst NICHT übernommen (anders als die Soft-Expiry-Regel für eine bereits
    /// installierte Lizenz zur Laufzeit) - ein frisch eingespielter Schlüssel, der schon tot
    /// ist, nützt nichts und sähe als "aktiviert" nur falsch erfolgreich aus.
    /// </summary>
    public static LicenseImportDiagnosis ImportFromKeyText(string keyText, Guid ownCustomerGroupId, byte[] publicKeyBytes) =>
        ImportFromKeyText(keyText, ownCustomerGroupId, AppPaths.LicenseFilePath, publicKeyBytes);

    internal static LicenseImportDiagnosis ImportFromKeyText(string keyText, Guid ownCustomerGroupId, string destinationFilePath, byte[] publicKeyBytes)
    {
        var license = LicenseReader.TryVerifyKeyTextAuthenticity(keyText, publicKeyBytes);
        if (license is null)
        {
            return new LicenseImportDiagnosis(LicenseImportOutcome.NotRecognized, null);
        }

        if (license.CustomerGroupId != ownCustomerGroupId)
        {
            return new LicenseImportDiagnosis(LicenseImportOutcome.WrongCustomer, license);
        }

        if (license.ExpiryDateUtc < DateTime.UtcNow)
        {
            return new LicenseImportDiagnosis(LicenseImportOutcome.Expired, license);
        }

        JsonFileStore.Save(destinationFilePath, license);
        return new LicenseImportDiagnosis(LicenseImportOutcome.Activated, license);
    }

    /// <summary>
    /// P2P-Übernahme einer per Boot-Call von einem Peer mitgeteilten Lizenz (Issue #59/#60-
    /// Nachtrag "Lizenz sofort verteilen"): anders als <see cref="ImportFromKeyText"/>
    /// (manueller Admin-Import) wird eine bereits abgelaufene Lizenz hier NICHT abgelehnt
    /// (Soft-Expiry gilt auch hier, wie beim Datei-Import oben) - ein Peer, der zufällig als
    /// Erster antwortet, könnte sonst eine eigentlich gültige, nur zufällig zuerst gesehene
    /// abgelaufene Momentaufnahme systematisch blockieren. Übernimmt nur, wenn Signatur und
    /// Kundengruppe passen UND die mitgeteilte Lizenz laut IssuedAtUtc echt neuer ist als die
    /// eigene (oder noch gar keine eigene vorliegt) - vermeidet Downgrade-Flapping durch eine
    /// ältere, aus dem Cache eines langsameren Peers stammende Kopie.
    /// </summary>
    public static bool TryAdoptFromPeer(string keyText, Guid ownCustomerGroupId, byte[] publicKeyBytes, License? currentLicense) =>
        TryAdoptFromPeer(keyText, ownCustomerGroupId, AppPaths.LicenseFilePath, publicKeyBytes, currentLicense);

    internal static bool TryAdoptFromPeer(string keyText, Guid ownCustomerGroupId, string destinationFilePath, byte[] publicKeyBytes, License? currentLicense)
    {
        var checkResult = LicenseReader.LoadFromKeyText(keyText, ownCustomerGroupId, publicKeyBytes);
        if (checkResult.License is null)
        {
            return false; // nicht lesbar, Signatur ungültig, oder falsche Kundengruppe
        }

        if (currentLicense is not null && checkResult.License.IssuedAtUtc <= currentLicense.IssuedAtUtc)
        {
            return false; // eigene Lizenz ist schon mindestens genauso aktuell
        }

        JsonFileStore.Save(destinationFilePath, checkResult.License);
        return true;
    }

    private static LicenseImportResult Persist(LicenseCheckResult checkResult, string destinationFilePath)
    {
        if (checkResult.Status is LicenseStatus.Invalid or LicenseStatus.Missing)
        {
            return new LicenseImportResult(false, checkResult);
        }

        // Soft-Expiry (CLAUDE.md): eine korrekt signierte, aber bereits abgelaufene Lizenz
        // wird trotzdem übernommen - genau wie eine ganz normal eingelesene (#19), nicht
        // strenger nur weil sie gerade importiert statt mitgeliefert wurde. Gilt nur für den
        // Datei-Import oben, nicht für ImportFromKeyText (siehe dortiger Kommentar).
        JsonFileStore.Save(destinationFilePath, checkResult.License!);
        return new LicenseImportResult(true, checkResult);
    }
}

public sealed record LicenseImportResult(bool Success, LicenseCheckResult CheckResult);

/// <summary>Feingranulares Ergebnis von <see cref="LicenseImporter.ImportFromKeyText(string, Guid)"/> - siehe dortige Begründung für die Abweichung von den vier <see cref="LicenseStatus"/>-Werten.</summary>
public enum LicenseImportOutcome
{
    /// <summary>Signatur gültig, richtige Kundengruppe, nicht abgelaufen - übernommen.</summary>
    Activated,

    /// <summary>Signatur gültig, richtige Kundengruppe, aber Ablaufdatum in der Vergangenheit - NICHT übernommen.</summary>
    Expired,

    /// <summary>Signatur gültig, aber für eine andere Kundengruppe ausgestellt - NICHT übernommen.</summary>
    WrongCustomer,

    /// <summary>Text nicht dekodierbar oder Signatur ungültig (manipuliert/kein Lizenzschlüssel) - NICHT übernommen.</summary>
    NotRecognized,
}

/// <summary><see cref="License"/> ist bei <see cref="LicenseImportOutcome.NotRecognized"/> immer <c>null</c> - in allen anderen Fällen war der Schlüssel authentisch lesbar, auch wenn er abgelehnt wurde.</summary>
public sealed record LicenseImportDiagnosis(LicenseImportOutcome Outcome, License? License);
