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
/// <see cref="LastSeenUtc"/> (Nutzerwunsch 15.08.2026, revisionssicheres Audit-Log): der
/// Zeitpunkt, zu dem DER INFORMANT dieses Gerät zuletzt selbst gesehen hat - nicht wann
/// WIR vom Gossip gehört haben. Gibt einem Admin auch ohne je selbst direkten Kontakt zum
/// jeweiligen Gerät ein netzwerkweites "zuletzt gesehen"-Signal, mit dem sich die
/// Aktualität/Vollständigkeit des über AuditIngestStore empfangenen Logs für dieses Gerät
/// grob einschätzen lässt ("Log-Stand endet bei X, Gerät zuletzt Y gesehen" statt reinem
/// Rätselraten). Kostet nur ein zusätzliches kleines Feld im ohnehin schon gegossipten
/// Eintrag, kein neuer Kanal, kein zusätzliches Datenschutz-Risiko.
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
    DateTimeOffset LastSeenUtc);
