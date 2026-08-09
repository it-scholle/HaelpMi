namespace HaelpMi.Core.Models;

/// <summary>
/// "Gruppe" - ein Zusammenschluss von Arbeitsplätzen/Räumen (Geräten), z. B. eine Etage
/// (Nutzer-Klarstellung 04.08.2026: die zuvor implementierte "Kreis"-Zwischenebene war
/// eine Fehlinterpretation - "Kreis" bezeichnete von Anfang an die kundennummernbasierte
/// Netz-Isolation, siehe CustomerGroupId, nie ein Dashboard-Objekt. Gruppe ist die
/// einzige organisatorische Einheit im Dashboard). Direkt eine Liste von Geräten, keine
/// weitere Indirektion. Welche Nutzer/Räume das im Detail sind, wird im Dashboard aus
/// den Mitgliedsgeräten abgeleitet angezeigt, nicht hier gespeichert.
/// </summary>
public sealed class DeviceGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public List<Guid> DeviceIds { get; set; } = new();

    /// <summary>
    /// True nur für die eine fest verdrahtete "Alle"-Gruppe (<see cref="AppConstants.AllDevicesGroupId"/>,
    /// Nutzerwunsch 09.08.2026). Name und Mitgliedschaft sind im Dashboard nicht editierbar -
    /// <see cref="DeviceIds"/> bleibt bei ihr immer leer/ungenutzt, RecipientResolver löst ihre
    /// Mitglieder stattdessen live aus allen aktuell per Gossip bekannten Geräten auf. Dadurch
    /// funktioniert "Alle" auch, wenn gerade kein Admin-Gerät online ist, um eine
    /// Mitgliederliste zu synchronisieren.
    /// </summary>
    public bool IsBuiltInAllDevicesGroup => Id == AppConstants.AllDevicesGroupId;
}
