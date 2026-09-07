using HaelpMi.Core.Models;

namespace HaelpMi.Core.Networking.Protocol;

/// <summary>Distinguishes an unsolicited boot-call announce from the direct reply to one (Teil 2, Abschnitt 9).</summary>
public enum MessageKind
{
    Announce,
    Reply,
}

/// <summary>
/// Wire format for the UDP boot-call channel (Teil 2, Abschnitt 9 - supersedes Phase 1's
/// plain start-broadcast). "Jeder Boot-Call ist Call-and-Response: beide beteiligten
/// Geräte tauschen ihren vollständigen Info-Stack aus, unabhängig davon, wer Absender
/// des ursprünglichen Calls war" - both Announce and Reply carry the exact same fields
/// for that reason; only <see cref="Kind"/> distinguishes them, purely so a receiver
/// knows whether it still owes a Reply back.
///
/// <see cref="ProgramVersion"/>/<see cref="ConfigVersion"/> are what let "das Gerät mit
/// der jeweils neueren Version" side of a boot-call detect and signal an available
/// update - comparison happens locally on each side, there is no separate
/// "update available" message.
///
/// <see cref="KnownDevices"/> (Nutzerwunsch 05.08.2026): nur auf einer <see cref="Reply"/>
/// gesetzt, nie auf einem Announce (das bliebe sonst ein Broadcast, den ALLE Geräte auf dem
/// Segment empfangen, nicht nur der eine, der es braucht - siehe DiscoveryService). Der
/// Antwortende hängt seine eigene Geräteliste an, damit der neu startende Announcer auch
/// von Geräten erfährt, die gerade offline sind. Optional/nullable, damit alte Announces
/// (die dieses Feld nie setzen) unverändert kompatibel bleiben.
///
/// <see cref="FirstSeenUtc"/> (Issue #59/#60): eigener Erstkontakt-Zeitpunkt des Absenders,
/// Grundlage für <see cref="HaelpMi.Core.Licensing.LicenseLimitEvaluator"/> - siehe
/// <see cref="Models.DeviceEntry.FirstSeenUtc"/>. Ebenfalls optional/nullable, gleicher
/// Kompatibilitätsgrund wie <see cref="KnownDevices"/>.
/// </summary>
public sealed record BootCallMessage(
    MessageKind Kind,
    Guid CustomerGroupId,
    Guid DeviceId,
    string ComputerName,
    string User,
    string RoomName,
    string RoomNumber,
    Role Role,
    bool IsRemoteSession,
    int TcpPort,
    string ProgramVersion,
    int ConfigVersion,
    DateTimeOffset SentAtUtc,
    IReadOnlyList<KnownDeviceSummary>? KnownDevices = null,
    DateTimeOffset? FirstSeenUtc = null);
