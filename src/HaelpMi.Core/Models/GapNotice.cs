namespace HaelpMi.Core.Models;

/// <summary>
/// Markiert eine erkannte Lücke in einer empfangenen <see cref="AuditLogEntry"/>-Kette
/// (siehe AuditIngestStore.Append) - ehrliche Unvollständigkeit statt vorgetäuschter
/// Vollständigkeit: kommt ein Eintrag an, dessen <see cref="AuditLogEntry.PrevHash"/> nicht
/// an den zuletzt gespeicherten Stand für dieses Ursprungsgerät anschließt (bzw. dessen Seq
/// nicht lückenlos folgt), wird die Lücke vermerkt statt still geschluckt oder das Paket
/// verworfen. Kein automatisches Nachfordern in dieser Ausbaustufe (kein Over-Engineering).
/// </summary>
public sealed record GapNotice(
    Guid OriginDeviceId,
    long ExpectedFromSeq,
    long ActualSeq,
    DateTimeOffset DetectedAtUtc);
