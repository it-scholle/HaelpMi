using System.Runtime.InteropServices;
using System.Text;

namespace HaelpMi.Core.Interop;

/// <summary>
/// Ermittelt den Anzeigenamen einer Taste über das Tastaturlayout statt über eine feste
/// Tabelle, damit layoutabhängige OEM-Tasten (^, Ü, ß, &lt; ...) ihr echtes Zeichen zeigen.
/// </summary>
internal static class KeyNameResolver
{
    private const uint MAPVK_VK_TO_VSC = 0;
    private const uint MAPVK_VK_TO_CHAR = 2;
    private const uint DeadKeyFlag = 0x80000000;
    private const int ExtendedKeyFlag = 1 << 24;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, StringBuilder lpString, int cchSize);

    public static string? Resolve(int virtualKeyCode) => Resolve(virtualKeyCode, GetKeyboardLayout(0));

    /// <param name="keyboardLayout">HKL des Layouts, gegen das aufgelöst wird.</param>
    internal static string? Resolve(int virtualKeyCode, IntPtr keyboardLayout)
    {
        var character = MapVirtualKeyEx((uint)virtualKeyCode, MAPVK_VK_TO_CHAR, keyboardLayout) & ~DeadKeyFlag;
        if (character > ' ')
        {
            return char.ToUpper((char)character).ToString();
        }

        // GetKeyNameText arbeitet auf Scancodes; ohne Extended-Bit liefert es für
        // Navigationstasten (Pos1, Entf, Pfeile ...) den Namen der Ziffernblock-Doppelbelegung.
        var scanCode = MapVirtualKeyEx((uint)virtualKeyCode, MAPVK_VK_TO_VSC, keyboardLayout);
        if (scanCode == 0)
        {
            return null;
        }
        var lParam = (int)(scanCode << 16) | (IsExtendedKey(virtualKeyCode) ? ExtendedKeyFlag : 0);
        var buffer = new StringBuilder(64);
        return GetKeyNameText(lParam, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    private static bool IsExtendedKey(int virtualKeyCode) => virtualKeyCode is
        >= 0x21 and <= 0x2E // Bild auf/ab, Ende, Pos1, Pfeile, Druck, Einfg, Entf
        or 0x5B or 0x5C or 0x5D // Win links/rechts, Menütaste
        or 0x6F // Ziffernblock-Division
        or 0x90 // Num-Lock
        or 0xA3 or 0xA5; // Strg/Alt rechts
}
