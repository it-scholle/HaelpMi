using System.ComponentModel;
using System.Runtime.InteropServices;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Licensing;

namespace HaelpMi.Agent;

/// <summary>Hinweis "Alarm nicht gesendet" nach einem Tastenkürzel auf einem gesperrten Gerät.</summary>
internal static class BlockedHintDialog
{
    private const uint MB_ICONWARNING = 0x30;
    private const uint MB_SETFOREGROUND = 0x10000;
    private const uint MB_TOPMOST = 0x40000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    // Bewusst Win32 statt WPF-MessageBox: die nimmt ohne Owner das aktive Fenster des Agents,
    // auf einem gesperrten Gerät meist den Sperr-Toast. Der Hinweis ist dann kein Fenster auf
    // oberster Ebene (UI Automation führt ihn unter dem Toast) und verschwindet, sobald der
    // Toast schließt.
    public static void Show(SendBlockNotice notice)
    {
        if (MessageBoxW(IntPtr.Zero, notice.Message, $"HälpMi - {notice.Title}", MB_ICONWARNING | MB_SETFOREGROUND | MB_TOPMOST) == 0)
        {
            CrashLogger.Log(nameof(HaelpMi.Agent), "Sperrhinweis konnte nicht angezeigt werden", new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }
}
