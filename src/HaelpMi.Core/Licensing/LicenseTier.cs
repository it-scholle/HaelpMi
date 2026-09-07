namespace HaelpMi.Core.Licensing;

/// <summary>
/// Lizenz-Pakete (Nutzerabstimmung 27.08.2026, siehe Issue #18): Trial=10 Nutzer, S=25,
/// M=75, L=150, XL=unbegrenzt (<see cref="License.UserLimit"/> ist bei XL <c>null</c>).
/// Die konkrete Staffelung selbst ist Teil der signierten <see cref="License"/>, nicht
/// hier als Konstante hinterlegt - dieses Enum bildet nur die Paketbezeichnung ab.
/// <c>Trial</c> heißt als Anzeigename seit Einführung eines offiziellen, gleich großen
/// XS-Pakets "XS/Trial" (siehe <c>LicenseTierLimits.GetDisplayLabel</c> im Install-Creator)
/// - der Enum-Name selbst bleibt <c>Trial</c>, da er Teil der signierten Bytes ist
/// (<see cref="License.GetSigningPayload"/>).
/// <c>Custom</c> (ergänzt für Testlizenzen mit frei wählbarer, auch sehr kleiner
/// Geräteanzahl, z. B. 2 Geräte für einen VM-Testaufbau) trägt kein festes
/// <see cref="License.UserLimit"/> - der Wert kommt beim Ausstellen frei aus dem
/// Install-Creator statt aus einer Staffel.
/// </summary>
public enum LicenseTier
{
    Trial,
    S,
    M,
    L,
    XL,
    Custom,
}
