using System.Runtime.InteropServices;
using System.Windows.Forms;
using HaelpMi.Core.Models;

namespace HaelpMi.Core.Interop;

/// <summary>
/// Wraps Win32 RegisterHotKey (5.3) so a configured hotkey fires even while a different,
/// possibly fullscreen, application has focus. RegisterHotKey delivers WM_HOTKEY to a
/// window's message queue, so this creates a message-only window (HWND_MESSAGE) via
/// <see cref="NativeWindow"/> purely to receive it - no visible window is ever shown for
/// this. Must be constructed on the same thread that pumps Win32 messages (the WPF
/// UI/Dispatcher thread).
///
/// Teil 2, FR-42: der Hotkey ist nicht mehr ein einzelner, sondern einer pro
/// <see cref="AlarmProfile"/> (admin-verwaltet). RegisterHotKey braucht pro Registrierung
/// eine eigene kleine Ganzzahl-ID - diese Klasse verwaltet die Zuordnung
/// Win32-ID -&gt; Profil-ID intern, damit Aufrufer nur noch mit Profil-GUIDs arbeiten.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private static readonly IntPtr HwndMessage = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed class MessageOnlyWindow : NativeWindow
    {
        public event Action<int>? HotkeyMessageReceived;

        public MessageOnlyWindow()
        {
            CreateHandle(new CreateParams { Parent = HwndMessage });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                HotkeyMessageReceived?.Invoke(m.WParam.ToInt32());
            }
            base.WndProc(ref m);
        }
    }

    private readonly MessageOnlyWindow _window = new();
    private readonly Dictionary<int, Guid> _idToProfileId = new();
    private int _nextId = 1;

    /// <summary>Raised on the thread that constructed this instance when a registered profile's hotkey fires.</summary>
    public event Action<Guid>? ProfilePressed;

    public GlobalHotkey()
    {
        _window.HotkeyMessageReceived += id =>
        {
            if (_idToProfileId.TryGetValue(id, out var profileId))
            {
                ProfilePressed?.Invoke(profileId);
            }
        };
    }

    /// <summary>Unregisters everything, then registers one hotkey per profile (skips profiles with no hotkey). Returns per-profile errors, if any.</summary>
    public IReadOnlyDictionary<Guid, string> ReplaceAll(IEnumerable<(Guid ProfileId, HotkeyDefinition? Hotkey)> profiles)
    {
        UnregisterAll();

        var errors = new Dictionary<Guid, string>();
        foreach (var (profileId, hotkey) in profiles)
        {
            if (hotkey is null)
            {
                continue;
            }

            var id = _nextId++;
            if (RegisterHotKey(_window.Handle, id, (uint)hotkey.Modifiers, (uint)hotkey.VirtualKeyCode))
            {
                _idToProfileId[id] = profileId;
            }
            else
            {
                var win32Error = Marshal.GetLastWin32Error();
                errors[profileId] = $"Tastenkürzel \"{hotkey.Format()}\" konnte nicht registriert werden (Win32-Fehler {win32Error}) - vermutlich bereits durch ein anderes Programm belegt.";
            }
        }

        return errors;
    }

    public void UnregisterAll()
    {
        foreach (var id in _idToProfileId.Keys)
        {
            UnregisterHotKey(_window.Handle, id);
        }
        _idToProfileId.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.DestroyHandle();
    }
}
