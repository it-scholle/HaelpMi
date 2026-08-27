using System;
using HaelpMi.InstallCreator.Licensing;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Eigene Bookkeeping-Kopie (Anbieter-Sicht) einer ausgestellten Lizenz, bindet sie an genau
/// eine Kundengruppe (<see cref="CustomerGroupId"/>) - ein reines Ablaufdatum (wie zunächst in
/// #21 vorgesehen) enthält keine solche Bindung. Unabhängig von der signierten Datei, die der
/// Kunde erhält (<see cref="Licensing.SignedLicenseFile"/>): dieser Eintrag dient nur der
/// eigenen Übersicht (#21), enthält deshalb keine Signatur.
/// </summary>
internal sealed record LicenseRegistryEntry(
    Guid LizenzId,
    Guid CustomerGroupId,
    LicenseTier Tier,
    int? UserLimit,
    DateTime ErstelltAm,
    DateTime Ablaufdatum);
