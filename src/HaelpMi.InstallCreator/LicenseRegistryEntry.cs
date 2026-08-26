using System;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Vorarbeit zu #18/#19 (Lizenzerstellung/-prüfung): bindet eine Lizenz an genau eine
/// Kundengruppe (<see cref="CustomerGroupId"/>), damit sie nicht einfach an eine andere
/// Installation weitergereicht werden kann - ein reines Ablaufdatum (wie zunächst in #21
/// vorgesehen) enthält keine solche Bindung. Die eigentliche Erzeugung/Signierung (Ed25519,
/// siehe CLAUDE.md "Kunden-Lizenzsignatur") sowie die Prüfung beim Kunden (Abgleich der
/// eigenen CustomerGroupId gegen dieses Feld) gehören zu #18/#19, nicht zu diesem Ticket -
/// dieser Datensatz ist nur die Struktur dafür.
/// </summary>
internal sealed record LicenseRegistryEntry(
    Guid LizenzId,
    Guid CustomerGroupId,
    DateTime Ablaufdatum);
