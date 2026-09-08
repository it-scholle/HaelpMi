namespace HaelpMi.Core.Licensing;

/// <summary>
/// Issue #61-Nacharbeit (Nutzerbericht 08.09.2026, "ich kann auf 3/2 Lizenzen gehen"): ob
/// sich ein Gerät im Geräte-Tab gerade AKTIVIEREN lässt - eine reine UI-Gate-Entscheidung,
/// getrennt von <see cref="LicenseLimitEvaluator"/> (der bestimmt, welche Geräte tatsächlich
/// aktiv sind), damit sie ohne WPF per xUnit testbar bleibt.
/// </summary>
public static class DeviceActivationGate
{
    /// <summary>
    /// Ein bereits aktives Gerät lässt sich immer deaktivieren (das gibt gerade den Platz für
    /// ein anderes Gerät frei) - nur das AKTIVIEREN eines noch inaktiven Geräts ist gesperrt,
    /// solange das Kontingent (<paramref name="seatLimit"/>, <c>null</c> = unbegrenzt) schon
    /// voll ausgeschöpft ist.
    /// </summary>
    public static bool CanToggle(bool isActive, int activeCount, int? seatLimit) =>
        isActive || seatLimit is null || activeCount < seatLimit.Value;
}
