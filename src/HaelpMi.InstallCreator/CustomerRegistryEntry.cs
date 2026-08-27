using System;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Ein Eintrag im lokalen Kundenregister (Issue #31/#21) - pro erstelltem Admin-Installer
/// genau einer. Test- und Produktivinstaller landen seit #43 in getrennten Dateien
/// (<see cref="CustomerRegistryStore"/>), daher kein eigenes Test-Flag mehr im Eintrag selbst.
/// Bewusst ohne Lizenz-Ablaufdatum: das gehört an die Lizenz selbst, gebunden an die
/// CustomerGroupId (<see cref="LicenseRegistryEntry"/>, Vorarbeit #18/#19), nicht redundant
/// hier ins Kundenregister dupliziert. <see cref="Kontakt"/> bleibt als einziges reserviertes
/// Feld für #21, noch ungenutzt.
/// </summary>
internal sealed record CustomerRegistryEntry(
    int Kundennummer,
    Guid CustomerGroupId,
    string Kundenname,
    DateTime ErstelltAm,
    string? Kontakt);
