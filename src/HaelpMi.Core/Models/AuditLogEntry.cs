namespace HaelpMi.Core.Models;

/// <summary>
/// Ein einzelner, hash-verketteter Audit-Log-Eintrag (Nutzerwunsch 14.08.2026:
/// revisionssicheres Protokoll ohne zentrale Instanz - siehe AuditLog/AuditSyncService).
/// <see cref="Seq"/> ist pro <see cref="OriginDeviceId"/> fortlaufend und lückenlos bei 1
/// beginnend; <see cref="EntryHash"/> = SHA256(<see cref="PrevHash"/> || Seq || Timestamp ||
/// Content), sodass eine nachträgliche Änderung an einem Eintrag die Kette ab dieser Stelle
/// erkennbar bricht. Der eigentliche Manipulationsschutz kommt aber erst durch externe
/// Zeugenschaft (mind. ein Admin-Gerät hat eine eigene Kopie, siehe AuditSyncService), nicht
/// durch die Hash-Chain allein - ein Angreifer mit vollem Dateisystemzugriff auf das
/// Ursprungsgerät könnte die Kette sonst komplett neu durchrechnen.
/// </summary>
public sealed record AuditLogEntry(
    long Seq,
    Guid OriginDeviceId,
    DateTimeOffset TimestampUtc,
    string PrevHash,
    string EntryHash,
    string Content);
