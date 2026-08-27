namespace HaelpMi.Core.Licensing;

/// <summary>
/// Ergebnis von <see cref="LicenseReader.Load(Guid)"/>. <see cref="License"/> ist nur bei
/// <see cref="LicenseStatus.Valid"/> oder <see cref="LicenseStatus.Expired"/> gesetzt - bei
/// <see cref="LicenseStatus.Invalid"/> ist den gelesenen Feldern nicht zu trauen (die
/// Signatur- bzw. Kundengruppenprüfung ist genau dafür da), bei
/// <see cref="LicenseStatus.Missing"/> gibt es keine Datei zum Lesen.
/// </summary>
public sealed record LicenseCheckResult(LicenseStatus Status, License? License);
