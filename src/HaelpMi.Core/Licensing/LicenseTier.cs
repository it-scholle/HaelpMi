namespace HaelpMi.Core.Licensing;

/// <summary>
/// Lizenz-Pakete (Nutzerabstimmung 27.08.2026, siehe Issue #18): Trial=10 Nutzer, S=25,
/// M=75, L=150, XL=unbegrenzt (<see cref="License.UserLimit"/> ist bei XL <c>null</c>).
/// Die konkrete Staffelung selbst ist Teil der signierten <see cref="License"/>, nicht
/// hier als Konstante hinterlegt - dieses Enum bildet nur die Paketbezeichnung ab.
/// </summary>
public enum LicenseTier
{
    Trial,
    S,
    M,
    L,
    XL,
}
