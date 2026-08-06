using System.Windows.Input;
using HaelpMi.Core.Models;

namespace HaelpMi.UI.Helpers;

/// <summary>Converts a WPF key press into a Core <see cref="HotkeyDefinition"/> for hotkey-capture text boxes.</summary>
public static class HotkeyInputHelper
{
    /// <summary>Returns null if the key pressed is a bare modifier (Ctrl/Alt/Shift/Win alone) - not a usable hotkey.</summary>
    public static HotkeyDefinition? TryCapture(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierOnly(key))
        {
            return null;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        var virtualKeyCode = KeyInterop.VirtualKeyFromKey(key);
        return new HotkeyDefinition(modifiers, virtualKeyCode);
    }

    private static bool IsModifierOnly(Key key) => key is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin or Key.System or Key.None;
}
