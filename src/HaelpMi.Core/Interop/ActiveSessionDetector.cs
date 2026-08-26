using System.Runtime.InteropServices;

namespace HaelpMi.Core.Interop;

/// <summary>
/// Bugfix 25.08.2026 (Issue #9-Nachtrag): AlarmChannel (Fast User Switching) zeigt ein
/// empfangenes Alarmpopup in JEDER angemeldeten Sitzung, egal ob Primary oder Satellite -
/// eine weggeschaltete Sitzung hat aber den Windows-Zustand WTSDisconnected, niemand kann
/// dort ein erzwungenes Popup sehen oder wegklicken. Prüft deshalb vor dem Anzeigen, ob die
/// EIGENE Sitzung gerade WTSActive ist (Konsole ODER eine aktiv verbundene RDP-Sitzung).
///
/// Bewusst NICHT WTSGetActiveConsoleSessionId() (prüft nur die physische Konsole - würde
/// eine legitime aktive RDP-Sitzung fälschlich als "inaktiv" behandeln, obwohl dort gerade
/// wirklich jemand sitzt).
/// </summary>
public static class ActiveSessionDetector
{
    private const int WtsCurrentSession = -1;
    private const int WtsConnectStateInfoClass = 8; // WTS_INFO_CLASS.WTSConnectState
    private const int WtsActive = 0; // WTS_CONNECTSTATE_CLASS.WTSActive

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer, int sessionId, int wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    /// <summary>
    /// Best-effort wie RemoteSessionDetector: schlägt die Abfrage selbst fehl, wird im
    /// Zweifel NICHT unterdrückt (true) - ein möglicherweise unsichtbares Popup ist beim
    /// Alarmsystem das kleinere Risiko als eines, das durch einen API-Fehlschlag komplett
    /// verschluckt wird.
    /// </summary>
    public static bool IsCurrentSessionActive()
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, WtsCurrentSession, WtsConnectStateInfoClass, out var buffer, out _))
        {
            return true;
        }

        try
        {
            return Marshal.ReadInt32(buffer) == WtsActive;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }
}
