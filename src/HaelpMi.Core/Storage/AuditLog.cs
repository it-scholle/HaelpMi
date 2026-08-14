using System.Text.Json;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>
/// "Wer hat wann was ausgelöst/empfangen"-Trail (NFR-7), weiterhin bewusst nie mit der
/// Alarmtext selbst gefüttert (NFR-5, Datenminimierung) - nur Geräte-IDs und Zeitstempel.
/// Es gibt weiterhin keinen zentralen Log-Server - jedes Gerät schreibt nur seinen eigenen
/// Ausschnitt (siehe Klassendoku-Historie unten); die Ablage landet weiterhin (wie
/// crash.log) auf dem geteilten Alpha-Test-Laufwerk statt ausschließlich lokal.
///
/// Erweiterung 14.08.2026 (Nutzerwunsch "revisionssicheres Protokoll"): jeder Eintrag ist
/// jetzt Teil einer Hash-Chain (<see cref="AuditLogEntry.PrevHash"/>/<see cref="AuditLogEntry.EntryHash"/>
/// = SHA256(PrevHash || Seq || Timestamp || Content)) statt einer schlichten Tab-Zeile -
/// eine nachträgliche Änderung an einer bereits geschriebenen Zeile bricht die Kette ab
/// dieser Stelle erkennbar. Datei heißt deshalb jetzt "audit.jsonl" statt "audit.log" (ein
/// altes "audit.log" bleibt als Alt-Stand liegen, aber ungelesen - in der Alpha-Phase
/// bewusst kein Migrationspfad, reine Anzeige-/Diagnose-Historie, kein Betriebsdatenverlust,
/// gleiches Prinzip wie beim ConfigHistoryStore-JsonException-Fallback). Der eigentliche
/// Manipulationsschutz kommt erst durch AuditSyncService (Push an mind. einen erreichbaren
/// Admin) - eine reine lokale Hash-Chain schützt allein nicht vor einem Angreifer mit vollem
/// Dateisystemzugriff auf dieses Gerät, siehe AuditLogEntry-Klassendoku.
///
/// Bugfix 11.08.2026 (Fehlerbericht "Autostart nicht eingerichtet", PERSONALBÜRO): bisher
/// AppPaths.RootFolder direkt, also NUR lokal unter %ProgramData% - für die
/// Alpha-Fehlersuche unerreichbar, während crash.log längst auf Z: lag (siehe
/// SharedLogPaths-Kommentar). Genau deshalb ließ sich die konkrete schtasks-Fehlermeldung
/// zum gemeldeten Vorfall nicht mehr nachträglich einsehen.
/// </summary>
public sealed class AuditLog
{
    private const string FileName = "audit.jsonl";
    private const string ChainStateFileName = "audit.chain-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly object _writeLock = new();
    private readonly Func<Guid> _deviceIdProvider;
    private long? _lastSeq;
    private string? _lastHash;

    /// <param name="deviceIdProvider">
    /// Lazy wie bei DiscoveryService/ConfigSyncService (Func statt fixem Wert) - beim
    /// Konstruieren (oft ein Feld-Initialisierer, bevor Settings/Deployment geladen sind)
    /// steht die eigene Geräte-ID typischerweise noch nicht fest, erst beim ersten
    /// tatsächlichen Append()-Aufruf.
    /// </param>
    public AuditLog(Func<Guid> deviceIdProvider)
    {
        _deviceIdProvider = deviceIdProvider;
    }

    public void Append(string entry)
    {
        try
        {
            var dir = SharedLogPaths.ResolveDirectory(AppPaths.RootFolder);
            lock (_writeLock)
            {
                EnsureChainStateLoaded(dir);

                var seq = (_lastSeq ?? 0) + 1;
                var prevHash = _lastHash ?? string.Empty;
                var timestamp = DateTimeOffset.UtcNow;
                var entryHash = AuditHashChain.Compute(prevHash, seq, timestamp, entry);
                var record = new AuditLogEntry(seq, _deviceIdProvider(), timestamp, prevHash, entryHash, entry);

                File.AppendAllText(Path.Combine(dir, FileName), JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);

                _lastSeq = seq;
                _lastHash = entryHash;
                JsonFileStore.Save(Path.Combine(dir, ChainStateFileName), new ChainState(seq, entryHash));
            }
        }
        catch (IOException)
        {
            // Audit logging is best-effort; a locked/unavailable log file must never
            // block sending or receiving an alarm.
        }
    }

    /// <summary>
    /// Liest bis zu <paramref name="maxCount"/> Einträge mit Seq &gt; <paramref name="afterSeq"/>,
    /// aufsteigend sortiert - für AuditSyncService.PushPendingAsync (Delta pro Ziel-Admin,
    /// siehe dortige Klassendoku). Bewusst ein voller Datei-Scan statt eines separaten
    /// Index (kein Over-Engineering für die hier erwarteten Log-Größen eines LAN-
    /// Alarmsystems; bei Bedarf später nachschärfbar).
    /// </summary>
    public IReadOnlyList<AuditLogEntry> ReadSince(long afterSeq, int maxCount)
    {
        var dir = SharedLogPaths.ResolveDirectory(AppPaths.RootFolder);
        var path = Path.Combine(dir, FileName);
        if (!File.Exists(path))
        {
            return Array.Empty<AuditLogEntry>();
        }

        var result = new List<AuditLogEntry>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AuditLogEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<AuditLogEntry>(line, JsonOptions);
            }
            catch (JsonException)
            {
                continue; // beschädigte/fremde Zeile - überspringen statt den ganzen Read abzubrechen
            }

            if (entry is null || entry.Seq <= afterSeq)
            {
                continue;
            }

            result.Add(entry);
            if (result.Count >= maxCount)
            {
                break;
            }
        }

        return result;
    }

    private void EnsureChainStateLoaded(string dir)
    {
        if (_lastSeq is not null)
        {
            return;
        }

        var state = JsonFileStore.Load<ChainState>(Path.Combine(dir, ChainStateFileName));
        _lastSeq = state?.LastSeq ?? 0;
        _lastHash = state?.LastHash ?? string.Empty;
    }

    private sealed record ChainState(long LastSeq, string LastHash);
}
