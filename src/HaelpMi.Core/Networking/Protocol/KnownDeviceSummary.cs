using HaelpMi.Core.Models;

namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Ein einzelner Eintrag im Gossip-Anhang einer Boot-Call-Antwort (Nutzerwunsch
/// 05.08.2026): "auf jeden Wakeup-Call antwortet der Empfänger mit seiner Anwesenheit UND
/// den Geräten, die er zusätzlich kennt" - so lernt ein neu startendes Gerät auch von
/// Geräten, die im Moment des eigenen Announce gerade NICHT gleichzeitig online waren
/// (reine Direkt-Antwort-Discovery kennt sonst nur, wer exakt jetzt gerade mithört). Nur
/// die für die Geräteliste nötigen Felder, keine lokalen Werte wie Favorite/Notiz - das
/// bleibt eine rein lokale Entscheidung jedes Geräts (siehe DeviceEntry).
///
/// <see cref="FirstSeenUtc"/> (Issue #59/#60): FirstSeenUtc des Gossip-Antragenden für
/// dieses Gerät, damit auch Drittwissen (nie direkt kontaktiert, nur über Gossip bekannt)
/// für <see cref="HaelpMi.Core.Licensing.LicenseLimitEvaluator"/> zur Verfügung steht. Optional/nullable
/// wie <see cref="BootCallMessage.KnownDevices"/> selbst, aus demselben
/// Kompatibilitätsgrund (ältere Programmversion mitten in einem Rollout).
/// </summary>
public sealed record KnownDeviceSummary(
    Guid DeviceId,
    string ComputerName,
    string User,
    string RoomName,
    string RoomNumber,
    Role Role,
    string IpAddress,
    int TcpPort,
    DateTimeOffset? FirstSeenUtc = null);
