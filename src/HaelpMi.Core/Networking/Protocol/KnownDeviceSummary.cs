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
///
/// <see cref="Override"/>/<see cref="OverrideSetAtUtc"/> (Issue #61): verbreitet eine im
/// Geräte-Tab getroffene Admin-Entscheidung ("deaktivieren/aktivieren") an Geräte, die den
/// Antragenden selbst nie direkt kontaktiert haben - derselbe Gossip-Mechanismus wie
/// <see cref="FirstSeenUtc"/>, mit "neuester Zeitstempel gewinnt" als Konfliktregel (siehe
/// DeviceStore.Upsert). Ohne eigene Admin-Signaturprüfung auf diesem Versionsstand
/// (CLAUDE.md) auf demselben Vertrauensniveau wie <see cref="Role"/> selbst - eine
/// unauthentifizierte, aber plausible Selbst-/Fremdauskunft, kein härteres Sicherheitsziel.
///
/// <see cref="Removed"/>/<see cref="RemovedSetAtUtc"/> (Issue #61-Nachtrag "Löschen
/// deaktiviert nicht wirklich"): die weiche, umkehrbare "Deinstalliert"-Markierung eines
/// ganz normalen <see cref="Models.DeviceEntry"/> (Selbstbericht per
/// <see cref="DiscoveryService.AnnounceSelfRemovedAsync"/> oder Drittmeinung) - "neuester
/// Zeitstempel gewinnt" wie bei <see cref="Override"/>, siehe DeviceStore.Upsert. Bewusst
/// NICHT der Weg für einen endgültigen <see cref="Storage.RemovedDeviceStore"/>-Tombstone
/// (Bugfix 10.09.2026, siehe <see cref="Protocol.PermanentlyRemovedDeviceSummary"/>): ein
/// Empfänger darf aus einer bloßen Removed=true-Drittmeinung nie selbst einen
/// unumkehrbaren lokalen Tombstone ableiten, sonst könnte jeder ungeprüfte Peer ein
/// beliebiges Gerät dauerhaft sperren.
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
    DateTimeOffset? FirstSeenUtc = null,
    LicenseOverride Override = LicenseOverride.None,
    DateTimeOffset? OverrideSetAtUtc = null,
    bool Removed = false,
    DateTimeOffset? RemovedSetAtUtc = null);
