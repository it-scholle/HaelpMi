namespace HaelpMi.Core.Licensing;

/// <summary>Ergebnis von <see cref="LicenseWarningEvaluator.Evaluate"/> - <see cref="DaysRemaining"/> ist nur bei einer bekannten <see cref="License.ExpiryDateUtc"/> gesetzt, also nicht bei Missing/Invalid.</summary>
public sealed record LicenseWarning(LicenseWarningLevel Level, int? DaysRemaining);
