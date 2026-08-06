namespace HaelpMi.Core.Storage;

/// <summary>
/// Minimal local-only "who triggered/received what, when" trail (NFR-7), deliberately
/// never given the alarm message text itself (NFR-5, Datenminimierung) - only device
/// ids and timestamps. Each device only ever sees its own local log; there is no
/// central log, consistent with "keine zentrale Instanz".
/// </summary>
public sealed class AuditLog
{
    private readonly object _writeLock = new();

    public void Append(string entry)
    {
        try
        {
            AppPaths.EnsureRootExists();
            lock (_writeLock)
            {
                File.AppendAllText(AppPaths.AuditLogFilePath, $"{DateTimeOffset.UtcNow:O}\t{entry}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Audit logging is best-effort; a locked/unavailable log file must never
            // block sending or receiving an alarm.
        }
    }
}
