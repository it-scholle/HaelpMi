using System.Runtime.InteropServices;

namespace HaelpMi.Core.Interop;

/// <summary>
/// Liest den echten, aktuellen Downloads-Ordner über die offizielle Windows-API statt
/// "%USERPROFILE%\Downloads" zu raten - falls der Nutzer den Ordner verschoben hat (z. B.
/// auf ein anderes Laufwerk), würde ein geratener Pfad einfach eine neue, falsche
/// Downloads-Struktur anlegen statt in den tatsächlich benutzten Ordner zu schreiben.
/// </summary>
public static class KnownFolders
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

    public static string GetDownloadsFolder()
    {
        try
        {
            if (SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out var pathPtr) == 0)
            {
                try
                {
                    var path = Marshal.PtrToStringUni(pathPtr);
                    if (!string.IsNullOrEmpty(path))
                    {
                        return path;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pathPtr);
                }
            }
        }
        catch (Exception)
        {
            // fällt unten auf den Standardpfad zurück
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
