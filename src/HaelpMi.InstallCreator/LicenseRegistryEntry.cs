using System;
using HaelpMi.InstallCreator.Controls;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Eigene Bookkeeping-Kopie (Anbieter-Sicht) einer ausgestellten Lizenz, bindet sie an genau
/// eine Kundengruppe (<see cref="CustomerGroupId"/>) - ein reines Ablaufdatum (wie zunächst in
/// #21 vorgesehen) enthält keine solche Bindung.
/// </summary>
/// <param name="KeyText">
/// Der vollständige, an den Kunden übermittelte Lizenzschlüssel-Text (Issue #54-Nacharbeit,
/// <see cref="Licensing.LicenseKeyText"/>) - bewusst hier mitgespeichert, damit der Anbieter
/// eine bereits ausgestellte Lizenz aus der Historie erneut kopieren/erneut versenden kann,
/// ohne sie neu zu signieren.
/// </param>
internal sealed record LicenseRegistryEntry(
    Guid LizenzId,
    Guid CustomerGroupId,
    LicenseTier Tier,
    int? UserLimit,
    DateTime ErstelltAm,
    DateTime Ablaufdatum,
    string KeyText);
