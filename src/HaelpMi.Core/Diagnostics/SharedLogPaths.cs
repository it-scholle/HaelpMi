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
    public static string ResolveDirectory(string localFallbackDir)
    {
        var sharedDir = Path.Combine(@"Z:\", "HaelpMi-Logs", Environment.MachineName);
        try
        {
            Directory.CreateDirectory(sharedDir);
            return sharedDir;
        }
        catch (Exception)
        {
            Directory.CreateDirectory(localFallbackDir);
            return localFallbackDir;
        }
    }
}
