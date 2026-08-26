using System;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Ein Eintrag im lokalen Kundenregister (Issue #31/#21) - pro erstelltem Admin-Installer
/// genau einer. Test- und Produktivinstaller landen seit #43 in getrennten Dateien
/// (<see cref="CustomerRegistryStore"/>), daher kein eigenes Test-Flag mehr im Eintrag selbst.
/// <see cref="LizenzAblauf"/> und <see cref="Kontakt"/> sind bewusst schon vorgesehen, aber noch
/// ungenutzt - Befüllung folgt erst mit #18/#19 (Lizenzprüfung), siehe #21.
/// </summary>
internal sealed record CustomerRegistryEntry(
    int Kundennummer,
    Guid CustomerGroupId,
    string Kundenname,
    DateTime ErstelltAm,
    string? LizenzAblauf,
    string? Kontakt);
