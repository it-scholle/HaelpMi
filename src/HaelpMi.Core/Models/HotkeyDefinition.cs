namespace HaelpMi.Core.Models;

/// <summary>
/// Modifier flags matching the Win32 RegisterHotKey MOD_* constants directly, so the
/// interop layer can pass <see cref="Modifiers"/> straight through without translation.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>
/// A single hotkey: modifier combination plus a Win32 virtual-key code. Deliberately
/// framework-agnostic (no System.Windows.Input.Key) so Core has no WPF dependency;
/// the WPF projects convert to/from <see cref="System.Windows.Input.Key"/> via
/// KeyInterop at the UI boundary.
/// </summary>
public sealed record HotkeyDefinition(HotkeyModifiers Modifiers, int VirtualKeyCode)
{
    private static readonly Dictionary<int, string> KeyNames = BuildKeyNames();

    /// <summary>Human-readable form for display, e.g. "Strg+Alt+H".</summary>
    public string Format()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Strg");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Umschalt");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyNames.TryGetValue(VirtualKeyCode, out var name) ? name : $"VK 0x{VirtualKeyCode:X2}");
        return string.Join("+", parts);
    }

    private static Dictionary<int, string> BuildKeyNames()
    {
        var map = new Dictionary<int, string>();
        for (var c = 'A'; c <= 'Z'; c++) map[c] = c.ToString();
        for (var c = '0'; c <= '9'; c++) map[c] = c.ToString();
        for (var f = 1; f <= 24; f++) map[0x70 + (f - 1)] = $"F{f}";
        return map;
    }
}
