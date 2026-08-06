namespace HaelpMi.Core.Updates;

/// <summary>
/// Begleitet ein Update-Paket (Abschnitt 11): von <c>HaelpMi.UpdateSigner sign</c> erzeugt,
/// über P2P zusammen mit dem eigentlichen Paket verteilt. <see cref="Sha256Hex"/> ist rein
/// informativ/fürs Log - die tatsächliche Prüfung (Integrität UND Echtheit in einem
/// Schritt) berechnet den Hash selbst neu, siehe <see cref="UpdatePackageVerifier"/>.
/// </summary>
public sealed record UpdatePackageManifest(
    string Version,
    string Sha256Hex,
    string SignatureBase64,
    DateTimeOffset BuiltAtUtc);
