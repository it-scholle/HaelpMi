using System;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Ein Eintrag im lokalen Kundenregister (Issue #31/#21) - pro erstelltem Admin-Installer
/// genau einer, auch für Test-Installer (die belegte Kundennummer soll in der Übersicht
/// sichtbar bleiben, nicht nur im Zähler verschwinden). <see cref="LizenzAblauf"/> und
/// <see cref="Kontakt"/> sind bewusst schon vorgesehen, aber noch ungenutzt - Befüllung folgt
/// erst mit #18/#19 (Lizenzprüfung), siehe #21.
/// </summary>
internal sealed record CustomerRegistryEntry(
    int Kundennummer,
    Guid CustomerGroupId,
    string Kundenname,
    bool IstTestInstaller,
    DateTime ErstelltAm,
    string? LizenzAblauf,
    string? Kontakt);
