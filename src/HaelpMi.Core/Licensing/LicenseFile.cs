using System.Text.Json;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Inhalt von license.json (FR-32): Kundenname, Sitzanzahl, Ablaufdatum, Ed25519-Signatur
/// über die drei fachlichen Felder. Wird von <see cref="HaelpMi.LicenseSigner"/> (siehe
/// Repository-Root) erzeugt und von <see cref="LicenseVerifier"/> geprüft.
///
/// ExpiresOnUtc ist bewusst ein DateOnly (Kalendertag der Kundenbeziehung, kein konkreter
/// Uhrzeitpunkt) - "Lizenz läuft am 31.12.2026 ab" braucht keine Uhrzeitgenauigkeit.
/// </summary>
public sealed record LicenseFile(string CustomerName, int SeatCount, DateOnly ExpiresOnUtc, string SignatureBase64)
{
    /// <summary>
    /// Die tatsächlich signierte/geprüfte Nutzlast: nur die drei fachlichen Felder, ohne
    /// die Signatur selbst. WICHTIG: diese exakte Feldform (Reihenfolge, Standard-
    /// JsonSerializerOptions ohne WriteIndented/Namenskonvertierung) muss byteidentisch zur
    /// gleichnamigen Methode in HaelpMi.LicenseSigner/LicenseSigningOperations.cs bleiben -
    /// ein Auseinanderlaufen bricht jede Signaturprüfung.
    /// </summary>
    public byte[] CanonicalPayload() =>
        JsonSerializer.SerializeToUtf8Bytes(new { CustomerName, SeatCount, ExpiresOnUtc });
}
