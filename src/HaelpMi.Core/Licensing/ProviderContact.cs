namespace HaelpMi.Core.Licensing;

/// <summary>
/// Feste Kontaktdaten des Anbieters, angezeigt bei Lizenz-Warnungen (Banner/Toast, Issue
/// #20) - fest im Produkt verdrahtet statt pro Kundengruppe, anders als der Lizenzschlüssel
/// selbst (#56): es gibt einen Anbieter für alle Installationen.
/// </summary>
public static class ProviderContact
{
    public const string Name = "Hannes Scholz";
    public const string Company = "IT-Scholle";

    public static string DisplayText => $"{Name} ({Company})";
}
