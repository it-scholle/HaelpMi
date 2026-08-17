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
    DateTimeOffset SentAtUtc,
    // Testmodus-Toggle (Nutzerwunsch 13.08.2026): additiv ans Ende angehängt, Default false
    // haelt einen alten Sender ohne dieses Feld sicher auf "kein Test" - nie faelschlich als
    // harmlos markiert. Wird nicht in IsPlausible() geprueft (bool ist immer plausibel,
    // analog SenderIsRemoteSession oben).
    bool IsTest = false);

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

/// <summary>
/// Rein lokales Umschlagformat für die Primary→Satellite-Weiterleitung eines bereits
/// empfangenen Alarms auf derselben Maschine (Fast-User-Switching-Fix 17.08.2026, s.
/// AlarmRelayServer/AlarmRelayClient). Kein Netzwerkformat - läuft ausschließlich über eine
/// lokale Named Pipe, deshalb kein SecureEnvelope/keine Verschlüsselung nötig: der Alarm wurde
/// bereits von der Primary-Instanz über <see cref="AlarmRequestMessage"/> entschlüsselt/geprüft.
/// </summary>
internal sealed record AlarmRelayMessage(AlarmRequestMessage Request, string SenderAddress);
