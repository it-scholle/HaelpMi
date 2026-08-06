namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Wire format for the TCP alarm channel (5.1/5.2): the sender connects, writes one of
/// these, then reads one <see cref="AlarmAckMessage"/> back on the same connection
/// before closing. A hotkey press starts a repeating send loop (Teil 2, Abschnitt 7:
/// every 5s) - all repeats of the same trigger share one <see cref="AlarmSessionId"/>,
/// generated once when the hotkey fires, so a receiver can tell "this is still the same
/// ongoing alarm" from "this is a brand new one" instead of opening a fresh popup every
/// 5 seconds.
/// </summary>
public sealed record AlarmRequestMessage(
    Guid CustomerGroupId,
    Guid AlarmProfileId,
    Guid AlarmSessionId,
    Guid SenderDeviceId,
    string SenderComputerName,
    string SenderUser,
    string SenderRoomName,
    string SenderRoomNumber,
    // FR-41: gilt auch für die Empfänger-Anzeige, nicht nur die eigene Geräteliste - NFR-13-Hinweis siehe Models.DeviceEntry.IsRemoteSession.
    bool SenderIsRemoteSession,
    string Text,
    int ResponseThreshold,
    DateTimeOffset SentAtUtc);

/// <summary>
/// Sent back immediately after the popup has been *displayed* (Phase 1 FR-13) - not
/// after "bin unterwegs" is clicked, that is a separate
/// <see cref="AlarmOnMyWayMessage"/> on the feedback channel.
/// </summary>
public sealed record AlarmAckMessage(
    Guid CustomerGroupId,
    Guid AlarmProfileId,
    Guid AlarmSessionId,
    Guid ReceiverDeviceId,
    DateTimeOffset ReceivedAtUtc);
