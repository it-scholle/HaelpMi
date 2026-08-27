using System;

namespace HaelpMi.InstallCreator.Licensing;

/// <summary>
/// Fachlicher Inhalt einer Lizenz, noch unsigniert - Format eingefroren in Issue #19 (Kommentar
/// 27.08.2026), damit die Prüfseite beim Kunden unabhängig von dieser UI dagegen gebaut werden
/// kann. <see cref="ExpiryDateUtc"/> wird für jeden Tier (inkl. Trial) manuell gesetzt, es gibt
/// keinen Standard-Laufzeit-Vorschlag.
/// </summary>
internal sealed record LicenseFile(
    Guid CustomerGroupId,
    LicenseTier Tier,
    int? UserLimit,
    DateTime IssuedAtUtc,
    DateTime ExpiryDateUtc);

/// <summary>
/// Auf der Festplatte abgelegte, signierte Form von <see cref="LicenseFile"/> - das eigentliche
/// an den Kunden ausgelieferte Artefakt. <see cref="Tier"/> als String statt Enum-Zahl, damit die
/// Datei auch ohne Kenntnis der Enum-Reihenfolge lesbar/debugbar bleibt.
/// </summary>
internal sealed record SignedLicenseFile(
    Guid CustomerGroupId,
    string Tier,
    int? UserLimit,
    DateTime IssuedAtUtc,
    DateTime ExpiryDateUtc,
    string SignatureBase64);
