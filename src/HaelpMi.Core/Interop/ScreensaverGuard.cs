using System.Runtime.InteropServices;

namespace HaelpMi.Core.Interop;

/// <summary>
/// Dismisses/prevents the screensaver while an alarm is being shown (FR-29). Uses only
/// the idle-timer / screensaver mechanism (SendInput to simulate activity,
/// SetThreadExecutionState to hold off the timer) - this deliberately never touches
/// the lock screen (FR-30): a locked session is a Windows security boundary that no
/// normal application can or should reach into, so a locked machine simply does not
/// get the forced popup, by design.
/// </summary>
public static class ScreensaverGuard
{
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    private const uint InputMouse = 0;
    private const uint MouseEventFMove = 0x0001;

    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    /// <summary>
    /// Simulates a tiny (net zero) mouse move. Real user-input events are exactly what
    /// dismisses an already-running screensaver and resets the system idle timer -
    /// there is no separate "stop screensaver" API to call instead.
    /// </summary>
    private static void SimulateActivity()
    {
        var inputs = new[]
        {
            new INPUT { type = InputMouse, u = new InputUnion { mi = new MOUSEINPUT { dx = 1, dy = 0, dwFlags = MouseEventFMove } } },
            new INPUT { type = InputMouse, u = new InputUnion { mi = new MOUSEINPUT { dx = -1, dy = 0, dwFlags = MouseEventFMove } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Dismisses any currently-running screensaver and holds off a new one (or display/system
    /// sleep) until disposed. Wrap the popup's visible lifetime in this.
    /// </summary>
    public static IDisposable SuppressWhileAlarmShown()
    {
        SimulateActivity();
        SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
        return new RestoreOnDispose();
    }

    private sealed class RestoreOnDispose : IDisposable
    {
        public void Dispose() => SetThreadExecutionState(EsContinuous);
    }
}
