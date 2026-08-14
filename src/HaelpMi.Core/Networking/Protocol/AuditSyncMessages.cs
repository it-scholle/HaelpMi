using HaelpMi.Core.Models;

namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// TCP-Push nicht bestätigter <see cref="AuditLogEntry"/>-Einträge an ein Admin-Gerät
/// (Nutzerwunsch 14.08.2026). <see cref="SenderDeviceId"/> ist, wer diese Verbindung
/// aufgebaut hat - das muss NICHT für jeden Eintrag <see cref="AuditLogEntry.OriginDeviceId"/>
/// entsprechen: beim Admin&lt;-&gt;Admin-Mesh-Abgleich (siehe AuditSyncService.
/// ReconcileWithAdminPeerAsync) leitet ein Admin auch Einträge ANDERER Ursprungsgeräte
/// weiter, die er selbst schon gesammelt hat.
/// </summary>
public sealed record AuditPushMessage(
    Guid CustomerGroupId,
    Guid SenderDeviceId,
    IReadOnlyList<AuditLogEntry> Entries,
    DateTimeOffset SentAtUtc);

/// <summary>
/// Antwort auf einen <see cref="AuditPushMessage"/>. <see cref="AcceptedUpToSeq"/> bezieht
/// sich nur auf die vom <see cref="AuditPushMessage.SenderDeviceId"/> selbst stammenden
/// Einträge (nicht auf weitergeleitete fremde) - genau das, was AuditSyncService.
/// PushPendingAsync als "für diesen Admin zugestellt" verbucht. <see cref="GapDetected"/>
/// zeigt an, dass mindestens eine Lücke in der Hash-Chain erkannt wurde (siehe GapNotice).
/// </summary>
public sealed record AuditPushAckMessage(
    Guid CustomerGroupId,
    bool Accepted,
    long AcceptedUpToSeq,
    bool GapDetected);

/// <summary>
/// Admin&lt;-&gt;Admin-Digest-Anfrage (Nutzerwunsch 15.08.2026, "bleeding edge" unter
/// mehreren gleichzeitig erreichbaren Admins): bewusst klein - nur die höchste bekannte
/// Seq je Ursprungsgerät, kein Log-Inhalt, gleiches Größenbudget-Prinzip wie die
/// Geräteliste (KnownDeviceSummary).
/// </summary>
public sealed record AuditDigestRequestMessage(
    Guid CustomerGroupId,
    Guid RequesterDeviceId,
    Dictionary<Guid, long> RequesterHighWaterMarks);

/// <summary>
/// Antwort auf eine <see cref="AuditDigestRequestMessage"/>: liefert in einem Aufwasch
/// sowohl den eigenen Digest (<see cref="ResponderHighWaterMarks"/>, für die Gegenrichtung -
/// der Requester pusht danach selbst über den normalen <see cref="AuditPushMessage"/>-Kanal
/// zurück, was er dem Antwortenden voraushat) als auch direkt die Einträge, bei denen der
/// Antwortende weiter ist als der Requester (<see cref="EntriesForRequester"/>) - spart eine
/// dritte Umlaufzeit.
/// </summary>
public sealed record AuditDigestResponseMessage(
    Guid CustomerGroupId,
    Dictionary<Guid, long> ResponderHighWaterMarks,
    IReadOnlyList<AuditLogEntry> EntriesForRequester);
