using System.Text.Json;
using System.Text.Json.Serialization;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Diagnostics;

/// <summary>Netzwerk-/UI-Richtung eines geloggten Ereignisses - "Local" für rein geräteinterne Vorgänge (Popup/Ton).</summary>
public enum TestLogDirection { Send, Receive, Local }

/// <summary>
/// Schweregrad einer geloggten Zeile, aufsteigend sortiert (Enum-Werte selbst bilden die
/// Vergleichsreihenfolge für <see cref="TestLogger.MinLevel"/>) - bewusst nur diese drei
/// Stufen (Nutzerwunsch), kein Debug/Trace ohne aktuell benannten Anwendungsfall.
/// </summary>
public enum TestLogLevel { Info, Warn, Error }

/// <summary>
/// Vokabular der über <see cref="TestLogger.LogAction"/> protokollierbaren Ereignistypen
/// (Flaw 19). Bewusst ein Enum statt freier Strings, damit Aufrufstellen (Flaw 20) über
/// IntelliSense/Compile-Time-Check finden, was es schon gibt, statt Tippfehler-Varianten
/// derselben Sache anzuhäufen.
/// </summary>
public enum TestLogEventType
{
    AlarmActivated,
    SelfTestStarted,
    MessageSent,
    MessageReceived,
    AckSent,
    AckReceived,
    CancelSent,
    CancelReceived,
    StatusChanged,
    ConnectionOpened,
    ConnectionClosed,

    // Lokale Präsentation auf dem Empfänger, keine Netzwerkaktion - wichtig, weil genau hier
    // (Netzwerkempfang bis zur tatsächlichen Anzeige/Audioausgabe) die in v0.35.2 nur händisch
    // mit StartupTimingLog untersuchte Verzögerung sichtbar wird.
    PopupShown,
    PopupClosed,
    SoundPlayed,
    SoundStopped,

    // Flaw-20-Ergänzungen (Instrumentierung der Kommunikationsschicht):
    // ActionSkipped - generischer Ausschluss-/Skip-Grund (z.B. Config-Sync/Discovery-
    // Exclusions). Bewusst NIE für CustomerGroupFilter-Ablehnungen verwendet (siehe
    // CustomerGroupFilter-Klassendoku: zwei unabhängige Installationen im selben Netz
    // dürfen sich nie gegenseitig entdecken, auch nicht über eine lokale Log-Zeile).
    // StartupMilestone - App-Start-Meilensteine, additiv zu StartupTimingLog (siehe dort,
    // bleibt für die laufende Verzögerungsuntersuchung bewusst overhead-arm bestehen).
    ActionSkipped,
    StartupMilestone,
}

/// <summary>Eine einzelne JSONL-Zeile - siehe <see cref="TestLogger"/>-Klassendoku für die Feldbedeutung.</summary>
internal sealed record TestLogEntry(
    DateTimeOffset TimestampUtc,
    string ProcessName,
    TestLogLevel Level,
    TestLogEventType EventType,
    TestLogDirection Direction,
    Guid LocalDeviceId,
    Guid? RemoteDeviceId,
    Guid? CorrelationId,
    string? Detail);

/// <summary>
/// Zentrales, leichtgewichtiges Test-Aktionsprotokoll (Nutzerwunsch: "im Testmodus wird JEDE
/// relevante Aktion strukturiert protokolliert"). Flaw 19 lieferte nur das Framework/die API;
/// Flaw 20 hat die eigentliche Instrumentierung der Kommunikationsschicht ergänzt (Aufrufe aus
/// AlarmFlowCoordinator, AlarmSender/AlarmTcpListener, RepeatingAlarmSession,
/// AlarmFeedbackChannel, ConfigSyncService, DiscoveryService, AlarmPopupWindow,
/// SenderStatusWindow, App.xaml.cs u.a. - siehe deren jeweilige TestLogger.LogAction-Aufrufe).
///
/// Gating läuft über zwei unabhängige Schwellen:
/// 1. <see cref="MinLevel"/> (Nutzerwunsch: "als Logging-Level einbauen") - Standard
///    <see cref="TestLogLevel.Error"/> für eine produktive Installation, <see cref="TestLogLevel.Info"/>
///    für einen Test-Installer (also praktisch alles, solange nicht manuell z.B. auf Warn
///    hochgesetzt - reiner In-Memory-Wert für diesen Prozesslauf, keine Persistierung/UI in
///    dieser Aufgabe, siehe <see cref="MinLevel"/>-Doku).
/// 2. Zielort: aktuell nur für <see cref="DeploymentInfo.IsTestInstaller"/>-Installationen
///    implementiert (Z:\HaelpMi-Logs\{MachineName} über <see cref="SharedLogPaths"/>, wie
///    AuditLog/CrashLogger/StartupTimingLog). Produktive Installationen haben laut Nutzerwunsch
///    später ebenfalls ein Ziel, aber lokal statt Netzlaufwerk - dieser Zielort ist noch nicht
///    festgelegt, deshalb schreibt <see cref="LogAction"/> dort bewusst noch nichts (obwohl
///    <see cref="MinLevel"/> für Produktiv schon korrekt auf Error steht, sodass beim
///    Nachrüsten des Zielorts nichts an der Schwellenlogik mehr geändert werden muss).
///
/// "Testmodus" ist damit NICHT der Laufzeit-Toggle <see cref="Models.TestModeArmState"/>
/// (dessen One-Shot-Sicherheitsgarantie "nächster Alarm ist Testdaten" bleibt unverändert ein
/// eigenständiges Konzept), sondern die Installationseigenschaft oben.
///
/// Bewusst NICHT über AuditLog selbst: dessen Hash-Chain + Sync-Bandbreite zu Admins pro
/// Eintrag wäre bei der hier erwarteten hohen Frequenz (jedes Senden/Empfangen/Ack) unnötiger
/// Overhead, siehe StartupTimingLog-Klassendoku für dieselbe Begründung an anderer Stelle.
///
/// Best-effort wie überall in dieser Diagnose-Familie: ein Schreibfehler (Z: weg, Datei
/// gesperrt, o.ä.) verwirft nur den einen Eintrag, blockiert/crasht aber nie den Aufrufer.
/// </summary>
public static class TestLogger
{
    private const string FileNamePrefix = "test-actions-";
    private const string FileNameSuffix = ".jsonl";
    private static readonly TimeSpan RetentionAge = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // Lazy = pro Prozesslauf genau eine deployment.json-Lesung, kein Datei-I/O pro LogAction-
    // Aufruf. Fehlt/kaputt deployment.json → sicher deaktiviert (produktiver Default), nie ein Absturz.
    private static readonly Lazy<bool> IsTestInstall = new(ResolveIsTestInstall);
    private static bool? _forcedIsTestInstallForTests;
    private static TestLogLevel? _minLevelOverride;
    private static readonly object CleanupLock = new();
    private static DateTime? _lastCleanupDateUtc;

    /// <summary>Vom jeweiligen Prozess einmal beim Start gesetzt (analog CrashLogger.InstallProcessWideHooks), taucht als ProcessName in jeder Zeile auf.</summary>
    private static string _processName = "?";

    public static void SetProcessName(string processName)
    {
        _processName = processName;
    }

    /// <summary>
    /// Mindest-Schweregrad, ab dem ein Eintrag tatsächlich geschrieben wird - Default abhängig
    /// von der Installationsart (Error für produktiv, Info für Test-Installer, siehe
    /// Klassendoku), aber jederzeit überschreibbar (Nutzerwunsch: "bis ich es auf Warn
    /// umstelle"). Bewusst ein einfacher In-Memory-Wert für diesen Prozesslauf - eine
    /// persistierte/admin-konfigurierbare Variante (z.B. über SharedConfig/Config-Sync) ist
    /// eine naheliegende spätere Erweiterung, aber kein aktuell benannter Anwendungsfall
    /// dieser Aufgabe (kein Over-Engineering vorab).
    /// </summary>
    public static TestLogLevel MinLevel
    {
        get => _minLevelOverride ?? (IsEffectivelyTestInstall() ? TestLogLevel.Info : TestLogLevel.Error);
        set => _minLevelOverride = value;
    }

    /// <param name="eventType">Was ist passiert.</param>
    /// <param name="level">Schweregrad - gegen <see cref="MinLevel"/> geprüft.</param>
    /// <param name="direction">Send/Receive für Netzwerkereignisse, Local für rein geräteinterne (z.B. Popup/Ton).</param>
    /// <param name="localDeviceId">Geräte-ID dieses Geräts.</param>
    /// <param name="correlationId">
    /// Meist die AlarmSessionId (siehe AlarmRequestMessage) - macht einen einzelnen
    /// Alarm-Vorgang über mehrere Zeilen und mehrere Geräte hinweg nachverfolgbar. Null für
    /// Ereignisse ohne zugehörigen Alarm (z.B. ein reiner Config-Sync-Verbindungsauf-/abbau).
    /// </param>
    /// <param name="remoteDeviceId">Geräte-ID des Peers, falls zutreffend (z.B. Sender/Empfänger, Verbindungspartner).</param>
    /// <param name="detail">
    /// Kurzer Status-/Grundtext (z.B. der neue Status bei StatusChanged) - NIEMALS Alarmtext
    /// oder sonstiger Nachrichteninhalt (NFR-5 Datenminimierung, wie AuditLog).
    /// </param>
    public static void LogAction(
        TestLogEventType eventType,
        TestLogLevel level,
        TestLogDirection direction,
        Guid localDeviceId,
        Guid? correlationId = null,
        Guid? remoteDeviceId = null,
        string? detail = null)
    {
        if (level < MinLevel)
        {
            return;
        }

        if (!IsEffectivelyTestInstall())
        {
            return; // produktiver Zielort noch nicht implementiert (siehe Klassendoku) - MinLevel oben steht aber schon korrekt auf Error
        }

        try
        {
            var dir = SharedLogPaths.ResolveDirectory(AppPaths.RootFolder);
            CleanupOldFilesOnce(dir);

            var entry = new TestLogEntry(
                DateTimeOffset.UtcNow,
                _processName,
                level,
                eventType,
                direction,
                localDeviceId,
                remoteDeviceId,
                correlationId,
                detail);

            var fileName = FileNamePrefix + DateTime.UtcNow.ToString("yyyy-MM-dd") + FileNameSuffix;
            File.AppendAllText(Path.Combine(dir, fileName), JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }
        catch (Exception)
        {
            // Test-Logging ist best-effort und darf den eigentlichen Vorgang (Alarm senden/
            // empfangen, Popup anzeigen, ...) nie blockieren oder zum Absturz bringen - ein
            // verworfener Log-Eintrag ist immer die richtige Wahl gegenüber einem Absturz.
        }
    }

    private static bool IsEffectivelyTestInstall() => _forcedIsTestInstallForTests ?? IsTestInstall.Value;

    /// <summary>
    /// Löscht abgelaufene test-actions-*.jsonl-Dateien im aufgelösten Verzeichnis - höchstens
    /// einmal pro Kalendertag (UTC) statt pro Zeile, kein eigener Hintergrund-Timer (Projekt-
    /// Grundsatz "kein Polling im Leerlauf"): Aufräumen hängt sich an einen ohnehin
    /// stattfindenden Schreibzugriff. Tagesbasiert statt "einmal pro Prozesslauf", weil der
    /// Agent-Prozess wochenlang durchlaufen kann (Fast User Switching) - eine reine
    /// Einmal-Markierung würde die Rotation nach dem ersten Tag dauerhaft abschalten.
    /// </summary>
    private static void CleanupOldFilesOnce(string dir)
    {
        var today = DateTime.UtcNow.Date;
        lock (CleanupLock)
        {
            if (_lastCleanupDateUtc == today)
            {
                return;
            }

            _lastCleanupDateUtc = today;
        }

        var cutoff = DateTime.UtcNow - RetentionAge;
        foreach (var file in Directory.EnumerateFiles(dir, FileNamePrefix + "*" + FileNameSuffix))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // einzelne fehlschlagende Löschung überspringen statt das Aufräumen abzubrechen
            }
        }
    }

    private static bool ResolveIsTestInstall()
    {
        try
        {
            return DeploymentInfoStore.Load().IsTestInstaller;
        }
        catch (Exception)
        {
            return false; // deployment.json fehlt/kaputt -> sicher produktiver Default statt Absturz
        }
    }

    /// <summary>Test-only hook (analog SharedLogPaths.ForceLocalFallbackForTests) - überschreibt <see cref="DeploymentInfo.IsTestInstaller"/> für die Dauer des Tests, ohne eine echte deployment.json zu brauchen.</summary>
    public static IDisposable ForceIsTestInstallerForTests(bool isTestInstaller)
    {
        var previous = _forcedIsTestInstallForTests;
        _forcedIsTestInstallForTests = isTestInstaller;
        return new RestoreOnDispose(() => _forcedIsTestInstallForTests = previous);
    }

    /// <summary>Test-only: setzt einen zuvor über <see cref="MinLevel"/> gesetzten Override zurück (wieder installationsabhängiger Default), damit ein Test keinen Zustand für den nächsten hinterlässt.</summary>
    internal static void ResetMinLevelOverrideForTests() => _minLevelOverride = null;

    /// <summary>Test-only: erzwingt, dass die nächste <see cref="LogAction"/> die Rotation erneut ausführt (sonst höchstens einmal pro Kalendertag, siehe <see cref="CleanupOldFilesOnce"/>).</summary>
    internal static void ResetCleanupStateForTests()
    {
        lock (CleanupLock)
        {
            _lastCleanupDateUtc = null;
        }
    }

    private sealed class RestoreOnDispose(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
