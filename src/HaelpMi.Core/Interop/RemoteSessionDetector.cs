using System.Runtime.InteropServices;

namespace HaelpMi.Core.Interop;

/// <summary>
/// Wraps GetSystemMetrics(SM_REMOTESESSION) (Teil 2, Abschnitt 3). Pure local session-
/// type query - no network call, no external dependency - and is treated purely as a
/// live display property (CLAUDE.md Datenschutz-Prinzipien): callers must not persist
/// this as a long-term history, only ever show it as part of a device's current status.
/// </summary>
public static class RemoteSessionDetector
{
    private const int SM_REMOTESESSION = 0x1000;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static bool IsCurrentSessionRemote() => GetSystemMetrics(SM_REMOTESESSION) != 0;
}
