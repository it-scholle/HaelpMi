using HaelpMi.Core.Models;

namespace HaelpMi.Core.Ipc;

/// <summary>
/// Bugfix 25.08.2026 (Issue #9): Named Pipes sind auf Windows sitzungsübergreifend
/// sichtbar (anders als "Local\"-Mutexe/-Events, siehe App.xaml.cs). Bei Fast User
/// Switching läuft je Sitzung ein eigener Agent mit eigenem IpcServer - ohne Sitzungsbezug
/// im Pipe-Namen würde Config.exe in Sitzung B nicht-deterministisch mit dem Agent aus
/// Sitzung A statt dem eigenen verbunden (beide Server-Instanzen bilden sonst einen
/// gemeinsamen Pool unter demselben Namen).
/// </summary>
internal static class IpcPipeNaming
{
    public static string BuildSessionScopedPipeName(int sessionId) => $"{AppConstants.IpcPipeName}.{sessionId}";
}
