using System.Diagnostics;
using System.Runtime.InteropServices;
using HaelpMi.Core.Diagnostics;

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

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfoGet(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

    // SPI_SETFOREGROUNDLOCKTIMEOUT erwartet den neuen Wert direkt in pvParam, nicht als Zeiger.
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfoSet(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

    private const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
    private const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
    private const int SW_RESTORE = 9;
    private const uint WM_NULL = 0x0000;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint ResponsiveProbeTimeoutMs = 250;
    private static readonly TimeSpan SlowWarningThreshold = TimeSpan.FromSeconds(1);
    private const string LogModul = "Vordergrund";

    /// <param name="vorgangsId">Für das Betriebs-Log, z. B. die Alarm-Session-ID.</param>
    public static void ForceForeground(IntPtr targetHwnd, string? vorgangsId = null)
    {
        var start = Stopwatch.GetTimestamp();
        var currentForeground = GetForegroundWindow();
        var currentThreadId = GetCurrentThreadId();
        var foregroundThreadId = GetWindowThreadProcessId(currentForeground, IntPtr.Zero);

        var attached = false;
        uint originalTimeout = 0;
        var timeoutRead = false;

        if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        {
            // Ohne Broadcast (fWinIni = 0): SPIF_SENDCHANGE schickte WM_SETTINGCHANGE synchron an alle
            // Top-Level-Fenster und konnte den UI-Thread sekundenlang festhalten.
            timeoutRead = SystemParametersInfoGet(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref originalTimeout, 0);
            SystemParametersInfoSet(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, 0);

            // Eine hängende Eingabewarteschlange anzuhängen würde unseren UI-Thread mit festhalten.
            if (IsResponsive(currentForeground))
            {
                attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }
            else
            {
                BetriebsLog.Warnung(LogModul, "Vordergrundfenster reagiert nicht, Eingabe wird nicht angehängt.", vorgangsId: vorgangsId);
            }
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
                SystemParametersInfoSet(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, (IntPtr)originalTimeout, 0);
            }
        }

        var dauer = Stopwatch.GetElapsedTime(start);
        var meldung = $"In den Vordergrund geholt in {dauer.TotalMilliseconds:F0} ms (angehängt: {attached}, Ziel vorne: {GetForegroundWindow() == targetHwnd}).";
        if (dauer > SlowWarningThreshold)
        {
            BetriebsLog.Warnung(LogModul, meldung, vorgangsId: vorgangsId);
        }
        else
        {
            BetriebsLog.Debug(LogModul, meldung, vorgangsId: vorgangsId);
        }
    }

    private static bool IsResponsive(IntPtr hwnd) =>
        SendMessageTimeout(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, ResponsiveProbeTimeoutMs, out _) != IntPtr.Zero;
}
