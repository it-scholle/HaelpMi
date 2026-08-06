namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Sent recipient -> sender when the "bin unterwegs" button is clicked (FR-51) - a
/// distinct, user-driven signal from the automatic display-ack (<see cref="AlarmAckMessage"/>).
/// One recipient can only meaningfully send this once per <see cref="AlarmSessionId"/>;
/// the sender is responsible for de-duplicating by (AlarmSessionId, ResponderDeviceId)
/// when counting toward the response threshold (FR-47).
///
/// Pflichtenheft NFR-13: carrying room + name + status here is additional personal-data
/// processing versus Version 1 and needs its own Personalrat sign-off (Abschnitt 8),
/// same caveat as the response log and the RDP flag - not a decision this codebase
/// makes on its own, just keeps the fields minimal (no free text, no history beyond the
/// existing per-device audit log).
/// </summary>
public sealed record AlarmOnMyWayMessage(
    Guid CustomerGroupId,
    Guid AlarmProfileId,
    Guid AlarmSessionId,
    Guid ResponderDeviceId,
    string ResponderComputerName,
    string ResponderUser,
    string ResponderRoomName,
    DateTimeOffset RespondedAtUtc);

/// <summary>
/// Sent sender -> every recipient (and echoed to the sender's own hover popup) whenever
/// the aggregate status of an ongoing alarm changes: a new ack, a new "bin unterwegs",
/// or the sender stopping (cancelled, threshold reached, or 5-minute timeout). This is
/// what lets a recipient's popup know it has crossed <see cref="AlarmRequestMessage.ResponseThreshold"/>
/// and may now be closed (FR-51), and what lets the sender's own hover popup show the
/// "Auf dem Weg" name list (FR-53) without polling.
/// </summary>
public sealed record AlarmStatusRelayMessage(
    Guid CustomerGroupId,
    Guid AlarmProfileId,
    Guid AlarmSessionId,
    int TargetCount,
    int AckedCount,
    IReadOnlyList<string> OnTheWayUserNames,
    bool SenderStillSending,
    DateTimeOffset SentAtUtc);
