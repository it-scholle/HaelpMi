using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Diagnostics;

/// <summary>
/// Temporäre Diagnose-Instrumentierung (Fehlerbericht "Selbsttest direkt nach
/// Admin-Installation ~1 Minute verzögert, nach Neustart sofort" - siehe Plan-Datei
/// "kontext-h-lpmi-direkt-nach-sleepy-lamport" für die volle Root-Cause-Untersuchung).
/// Markiert einzelne Meilensteine des Agent-Starts mit der seit Prozessstart vergangenen
/// Zeit, um empirisch zu belegen, WO genau die Verzögerung entsteht (vor oder nach
/// AutostartRegistrar.EnsureRegistered, vor oder nach dem eigentlichen TCP-Rundlauf).
///
/// Bewusst NICHT über <see cref="AuditLog"/>: dessen Hash-Chain + Schreibzugriff auf das
/// geteilte Alpha-Test-Laufwerk (Z:) pro Eintrag kostet selbst spürbar Zeit und würde genau
/// die Messung verfälschen, die hier stattfinden soll. Bewusst eine eigene, beim ersten
/// Zugriff gestartete Stopwatch statt Wall-Clock-Zeitstempel für die Differenzen - unabhängig
/// von Systemuhr-Drift/NTP-Sync kurz nach der Installation.
///
/// Kann nach Abschluss der Diagnose (Phase 1 des oben genannten Plans) wieder entfernt
/// werden - kein dauerhafter Architekturbestandteil, siehe CLAUDE.md "Kein Over-Engineering".
/// </summary>
public static class StartupTimingLog
{
    private static readonly System.Diagnostics.Stopwatch Stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public static void Mark(string processName, string milestone)
    {
        try
        {
            var dir = SharedLogPaths.ResolveDirectory(AppPaths.RootFolder);
            var entry = $"{DateTimeOffset.UtcNow:O}\t+{Stopwatch.ElapsedMilliseconds}ms\t{processName}\t{milestone}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, "startup-timing.log"), entry);
        }
        catch (Exception)
        {
            // Diagnose-Logging darf den eigentlichen Start nie blockieren oder zum Absturz bringen.
        }
    }
}
