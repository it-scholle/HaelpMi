using HaelpMi.Core.Diagnostics;

namespace HaelpMi.Core.Storage;

/// <summary>
/// Minimal "who triggered/received what, when" trail (NFR-7), deliberately never given
/// the alarm message text itself (NFR-5, Datenminimierung) - only device ids and
/// timestamps. There is no central log server, consistent with "keine zentrale Instanz" -
/// jedes Gerät schreibt weiterhin nur seinen eigenen Ausschnitt, nur die Ablage landet
/// (wie crash.log) auf dem geteilten Alpha-Test-Laufwerk statt ausschließlich lokal.
///
/// Bugfix 11.08.2026 (Fehlerbericht "Autostart nicht eingerichtet", PERSONALBÜRO): bisher
/// AppPaths.RootFolder direkt, also NUR lokal unter %ProgramData% - für die
/// Alpha-Fehlersuche unerreichbar, während crash.log längst auf Z: lag (siehe
/// SharedLogPaths-Kommentar). Genau deshalb ließ sich die konkrete schtasks-Fehlermeldung
/// zum gemeldeten Vorfall nicht mehr nachträglich einsehen.
/// </summary>
public sealed class AuditLog
{
    private readonly object _writeLock = new();

    public void Append(string entry)
    {
        try
        {
            var dir = SharedLogPaths.ResolveDirectory(AppPaths.RootFolder);
            lock (_writeLock)
            {
                File.AppendAllText(Path.Combine(dir, "audit.log"), $"{DateTimeOffset.UtcNow:O}\t{entry}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Audit logging is best-effort; a locked/unavailable log file must never
            // block sending or receiving an alarm.
        }
    }
}
