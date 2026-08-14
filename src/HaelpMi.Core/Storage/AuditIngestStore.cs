using System.Text.Json;
using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>Ergebnis eines Append-Aufrufs - für Audit-Log-Meldung/Ack-Nachricht.</summary>
public sealed record AuditIngestAppendResult(int AcceptedCount, bool GapDetected);

/// <summary>
/// Admin-seitige, nur-additive Ablage empfangener <see cref="AuditLogEntry"/>-Batches
/// (Nutzerwunsch 14.08.2026), eine Datei pro Ursprungsgerät unter
/// %ProgramData%\HaelpMi\audit-ingest\{OriginDeviceId}.jsonl. Additiv-only ist hier der
/// eigentliche Tamper-Evidence-Baustein: das Ursprungsgerät selbst kann diese Kopie nicht
/// mehr überschreiben/löschen, sobald sie hier liegt - eine spätere Abweichung zwischen
/// beiden Ständen wäre erkennbar.
///
/// Hash-Chain-Kontinuität wird beim Anhängen geprüft: passt <see cref="AuditLogEntry.PrevHash"/>
/// nicht lückenlos an den zuletzt gespeicherten Stand für dieses Ursprungsgerät an, wird die
/// Lücke als <see cref="GapNotice"/> in einer separaten Begleitdatei vermerkt statt still
/// geschluckt oder das ganze Paket verworfen - ehrliche Unvollständigkeit statt vorgetäuschter
/// Vollständigkeit. Kein automatisches Nachfordern/Backfill in dieser Ausbaustufe.
/// </summary>
public sealed class AuditIngestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly object _writeLock = new();

    private static string RootDir => Path.Combine(AppPaths.RootFolder, "audit-ingest");
    private static string EntriesPath(Guid originDeviceId) => Path.Combine(RootDir, $"{originDeviceId:N}.jsonl");
    private static string GapsPath(Guid originDeviceId) => Path.Combine(RootDir, $"{originDeviceId:N}.gaps.jsonl");

    /// <summary>Höchste bereits gespeicherte Seq je Ursprungsgerät - Grundlage für den Digest-Austausch (AuditSyncService.ReconcileWithAdminPeerAsync).</summary>
    public Dictionary<Guid, long> GetHighWaterMarks()
    {
        var result = new Dictionary<Guid, long>();
        if (!Directory.Exists(RootDir))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(RootDir, "*.jsonl"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParseExact(name, "N", out var originDeviceId))
            {
                continue; // z. B. eine *.gaps.jsonl-Begleitdatei - deren Name endet nicht auf ein reines 32-Hex-Guid
            }

            var tail = ReadChainTail(originDeviceId);
            if (tail.Seq is not null)
            {
                result[originDeviceId] = tail.Seq.Value;
            }
        }

        return result;
    }

    /// <summary>Bis zu <paramref name="maxCount"/> gespeicherte Einträge eines Ursprungsgeräts mit Seq &gt; <paramref name="afterSeq"/> - für den Mesh-Abgleich und einen künftigen Dashboard-Reader.</summary>
    public IReadOnlyList<AuditLogEntry> ReadSince(Guid originDeviceId, long afterSeq, int maxCount)
    {
        var path = EntriesPath(originDeviceId);
        if (!File.Exists(path))
        {
            return Array.Empty<AuditLogEntry>();
        }

        var result = new List<AuditLogEntry>();
        foreach (var line in File.ReadLines(path))
        {
            var entry = TryParseEntry(line);
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

    /// <summary>Hängt an - idempotent (schon vorhandene Seq werden übersprungen, wichtig bei Retry-Doppelzustellung) und lückenerkennend, siehe Klassendoku.</summary>
    public AuditIngestAppendResult Append(Guid originDeviceId, IReadOnlyList<AuditLogEntry> entries)
    {
        if (entries.Count == 0)
        {
            return new AuditIngestAppendResult(0, false);
        }

        lock (_writeLock)
        {
            Directory.CreateDirectory(RootDir);
            var (lastSeq, lastHash) = ReadChainTail(originDeviceId);
            var accepted = 0;
            var gapDetected = false;

            using var entriesWriter = new StreamWriter(EntriesPath(originDeviceId), append: true);
            StreamWriter? gapsWriter = null;
            try
            {
                foreach (var entry in entries.OrderBy(e => e.Seq))
                {
                    if (lastSeq is not null && entry.Seq <= lastSeq)
                    {
                        continue; // schon vorhanden (Doppelzustellung/Retry) - idempotent
                    }

                    // Zwei getrennte Prüfungen (Klassendoku): Kettenfortsetzung (passt Seq/
                    // PrevHash an das schon Gespeicherte an?) UND Inhaltskonsistenz (passt
                    // EntryHash tatsächlich zum transportierten Content? - ohne das wäre eine
                    // in sich konsistent weitergerechnete, aber komplett gefälschte Kette
                    // nicht von einer echten zu unterscheiden).
                    var expectedSeq = (lastSeq ?? 0) + 1;
                    var chainContinues = entry.Seq == expectedSeq && (lastHash is null || entry.PrevHash == lastHash);
                    var hashMatchesContent = entry.EntryHash == AuditHashChain.Compute(entry.PrevHash, entry.Seq, entry.TimestampUtc, entry.Content);
                    if (!chainContinues || !hashMatchesContent)
                    {
                        gapDetected = true;
                        gapsWriter ??= new StreamWriter(GapsPath(originDeviceId), append: true);
                        var gap = new GapNotice(originDeviceId, expectedSeq, entry.Seq, DateTimeOffset.UtcNow);
                        gapsWriter.WriteLine(JsonSerializer.Serialize(gap, JsonOptions));
                    }

                    entriesWriter.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
                    lastSeq = entry.Seq;
                    lastHash = entry.EntryHash;
                    accepted++;
                }
            }
            finally
            {
                gapsWriter?.Dispose();
            }

            return new AuditIngestAppendResult(accepted, gapDetected);
        }
    }

    private (long? Seq, string? Hash) ReadChainTail(Guid originDeviceId)
    {
        var path = EntriesPath(originDeviceId);
        if (!File.Exists(path))
        {
            return (null, null);
        }

        long? seq = null;
        string? hash = null;
        foreach (var line in File.ReadLines(path))
        {
            var entry = TryParseEntry(line);
            if (entry is null)
            {
                continue;
            }

            seq = entry.Seq;
            hash = entry.EntryHash;
        }

        return (seq, hash);
    }

    private static AuditLogEntry? TryParseEntry(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AuditLogEntry>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null; // beschädigte Zeile - überspringen statt den ganzen Read abzubrechen
        }
    }
}
