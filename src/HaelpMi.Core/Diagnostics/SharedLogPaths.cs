namespace HaelpMi.Core.Diagnostics;

/// <summary>
/// Alpha-Testphase: geteiltes Z:-Laufwerk, damit Diagnose-Dateien aller Test-Geräte an
/// einem Ort landen statt einzeln auf jeder Maschine gesucht werden zu müssen - pro
/// Rechnername ein eigener Unterordner, damit gleichzeitige Schreibzugriffe mehrerer
/// Geräte sich nicht in dieselbe Datei mischen. Fällt automatisch auf einen lokalen
/// Fallback-Ordner zurück, wenn Z: nicht erreichbar ist (z. B. spätere echte
/// Kunden-Installation ohne dieses Laufwerk) - die im CLAUDE.md-Gespräch vorgesehene
/// "nur nach Admin-Bestätigung"-Variante für die Produktivphase ist damit NICHT umgesetzt,
/// nur dieser pragmatische Alpha-Zwischenschritt.
///
/// Bugfix 11.08.2026: bisher nur in CrashLogger, per Kopie dort dupliziert - genau dieser
/// Split war das eigentliche Problem beim Nachvollziehen des "Autostart nicht
/// eingerichtet"-Fehlerberichts: crash.log landete auf Z:, die eigentliche schtasks-
/// Fehlermeldung (audit.log, siehe AutostartRegistrar/App.xaml.cs) aber nur lokal in
/// %ProgramData%, wo sie für die Ferndiagnose nicht erreichbar war. Jetzt eine
/// gemeinsame Stelle für beide, damit sie nie wieder auseinanderlaufen.
/// </summary>
internal static class SharedLogPaths
{
    private static bool _forceLocalFallbackForTests;

    // Fix 19.08.2026 (P1: Config-Sync/Alarm-Ack-Hänger, siehe AlarmFeedbackChannel/
    // ConfigSyncService/UI-Klick-Handler, die alle über TestLogger.LogAction hier landen):
    // Directory.CreateDirectory auf einem gemappten, aber gerade nicht erreichbaren
    // Z:-Laufwerk kann für die volle SMB-Timeout-Dauer blockieren (Sekunden bis Minuten),
    // BEVOR es überhaupt eine Exception wirft, die das try/catch unten fangen könnte - der
    // Aufruferthread (oft der UI-Thread oder eine Netzwerk-Empfangs-Continuation) friert in
    // dieser Zeit ein. Der Zugriffsversuch läuft deshalb jetzt in einer eigenen Task mit
    // hartem Timeout; überschreitet er ihn, wird sofort auf den lokalen Ordner
    // zurückgefallen, ohne auf das Ergebnis zu warten (das Ergebnis wird dann nie mehr
    // beobachtet, siehe ProbeReachability - absichtlich, ein "zu spät" gewordener
    // Netzwerkzugriff darf niemanden mehr interessieren). Kurzes Zwischenspeichern
    // (RecheckInterval) vermeidet außerdem, dass jeder einzelne Log-Aufruf erneut die volle
    // Probe-Zeit kostet, solange sich am Erreichbarkeitsstatus nichts ändert.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(30);
    private static readonly object CacheLock = new();
    private static string? _cachedDir;
    private static DateTime _cachedAtUtc = DateTime.MinValue;

    /// <summary>
    /// Test-only hook (14.08.2026, eingeführt für AuditLog/AuditSyncService-Tests): überspringt
    /// den Z:\-Versuch komplett. Ohne das würden hermetische Tests, die viele Append()-Aufrufe
    /// machen, auf einer Maschine mit real gemountetem Z:\HaelpMi-Logs (dieselbe physische
    /// Maschine wie die Alpha-Testrechner, siehe Klassenkommentar) echte Test-Dateien dort
    /// hinterlassen - AppPaths.UseRootForTests allein deckt nur den Fallback-Pfad ab, nicht
    /// den vorrangigen Z:\-Versuch.
    /// </summary>
    public static IDisposable ForceLocalFallbackForTests()
    {
        _forceLocalFallbackForTests = true;
        return new RestoreOnDispose(() =>
        {
            _forceLocalFallbackForTests = false;
            ResetReachabilityCacheForTests();
        });
    }

    /// <summary>Test-only: erzwingt eine frische Z:-Probe beim nächsten ResolveDirectory-Aufruf.</summary>
    public static void ResetReachabilityCacheForTests()
    {
        lock (CacheLock)
        {
            _cachedDir = null;
            _cachedAtUtc = DateTime.MinValue;
        }
    }

    public static string ResolveDirectory(string localFallbackDir)
    {
        if (_forceLocalFallbackForTests)
        {
            Directory.CreateDirectory(localFallbackDir);
            return localFallbackDir;
        }

        lock (CacheLock)
        {
            if (_cachedDir is not null && DateTime.UtcNow - _cachedAtUtc < RecheckInterval)
            {
                return _cachedDir;
            }
        }

        var sharedDir = Path.Combine(@"Z:\", "HaelpMi-Logs", Environment.MachineName);
        var resolved = ProbeReachability(sharedDir) ? sharedDir : FallBackLocal(localFallbackDir);

        lock (CacheLock)
        {
            _cachedDir = resolved;
            _cachedAtUtc = DateTime.UtcNow;
        }

        return resolved;
    }

    // internal statt private, damit ein Test die Zeitschranke direkt gegen einen bewusst
    // unerreichbaren Pfad prüfen kann, ohne den hartkodierten Z:\-Pfad oben anzufassen.
    internal static bool ProbeReachability(string sharedDir)
    {
        try
        {
            var probe = Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(sharedDir);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });

            // .Result nur, wenn die Task innerhalb des Timeouts fertig wurde - sonst würde
            // der Zugriff darauf selbst wieder blockieren. Läuft die Task danach doch noch
            // durch (Z: antwortet spät statt gar nicht), verhallt das Ergebnis ungenutzt.
            return probe.Wait(ProbeTimeout) && probe.Result;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string FallBackLocal(string localFallbackDir)
    {
        Directory.CreateDirectory(localFallbackDir);
        return localFallbackDir;
    }

    private sealed class RestoreOnDispose(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
