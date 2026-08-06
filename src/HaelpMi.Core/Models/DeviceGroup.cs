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
}
