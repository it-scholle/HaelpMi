using System.Runtime.InteropServices;

namespace HaelpMi.Core.Tests;

internal static class InstalledKeyboardLayouts
{
    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, IntPtr[]? lpList);

    /// <param name="layoutId">Layout-Kennung aus dem oberen Wort des HKL, z. B. 0x0407 für Deutsch.</param>
    /// <returns>Das installierte HKL oder <see cref="IntPtr.Zero"/>, falls das Layout fehlt.</returns>
    public static IntPtr Find(int layoutId)
    {
        var count = GetKeyboardLayoutList(0, null);
        var layouts = new IntPtr[count];
        GetKeyboardLayoutList(count, layouts);
        return layouts.FirstOrDefault(hkl => ((hkl.ToInt64() >> 16) & 0xFFFF) == layoutId);
    }
}
