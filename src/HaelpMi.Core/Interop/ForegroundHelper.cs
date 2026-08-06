using System.Runtime.InteropServices;

namespace HaelpMi.Core.Interop;

/// <summary>
/// Forces a window to the foreground even when Windows' foreground-stealing prevention
/// would normally block it - including over fullscreen applications (FR-9, 5.3, 6.).
/// Uses the documented AttachThreadInput + SPI_SETFOREGROUNDLOCKTIMEOUT workaround.
/// Call from the popup window's own code (e.g. on Loaded), passing its own HWND.
/// </summary>
public static class ForegroundHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

    private const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
    private const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
    private const uint SPIF_SENDCHANGE = 0x2;
    private const int SW_RESTORE = 9;

    public static void ForceForeground(IntPtr targetHwnd)
    {
        var currentForeground = GetForegroundWindow();
        var currentThreadId = GetCurrentThreadId();
        var foregroundThreadId = GetWindowThreadProcessId(currentForeground, IntPtr.Zero);

        var attached = false;
        uint originalTimeout = 0;
        var timeoutRead = false;

        if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        {
            timeoutRead = SystemParametersInfo(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref originalTimeout, 0);
            uint zero = 0;
            SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref zero, SPIF_SENDCHANGE);
            attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
        }

        try
        {
            ShowWindow(targetHwnd, SW_RESTORE);
            BringWindowToTop(targetHwnd);
            SetForegroundWindow(targetHwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }

            if (timeoutRead)
            {
                SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref originalTimeout, SPIF_SENDCHANGE);
            }
        }
    }
}
