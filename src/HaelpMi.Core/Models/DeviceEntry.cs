namespace HaelpMi.Core.Models;

/// <summary>
/// Locally stored view of one other device on the network (Phase 1 FR-18, 5.4).
/// ComputerName/User/Room/RoomNumber/Role/IsRemoteSession are supplied by that device
/// itself via its boot-call/announce and are never edited here - only Favorite,
/// Notified and Note are local decisions (FR-18).
/// </summary>
public sealed class DeviceEntry
{
    /// <summary>Persistent device identity (GUID) - the reconciliation key, never the IP (FR-3, FR-22).</summary>
    public required Guid DeviceId { get; set; }

    public string ComputerName { get; set; } = string.Empty;

    /// <summary>Currently logged-on Windows user on that device at the time it last announced - always live, never a stored "assignment" (Teil 2, Abschnitt 2).</summary>
    public string User { get; set; } = string.Empty;

    public string RoomName { get; set; } = string.Empty;

    public string RoomNumber { get; set; } = string.Empty;

    public Role Role { get; set; } = Role.User;

    /// <summary>
    /// Live session-type flag (Win32 SM_REMOTESESSION, FR-41), refreshed on every
    /// announce - a snapshot of "was this an RDP session as of the last time we heard
    /// from it", not a history log (CLAUDE.md Datenschutz-Prinzipien: this is a pure
    /// session property, never persisted as a long-term who-was-remote-when trail).
    /// Pflichtenheft NFR-13: showing this is additional personal-data processing versus
    /// Version 1 and needs its own Personalrat sign-off, same caveat as the response log.
    /// </summary>
    public bool IsRemoteSession { get; set; }

    /// <summary>Last known IP:port, refreshed on every broadcast/response (5.4). Used to actually send alarms.</summary>
    public string IpAddress { get; set; } = string.Empty;
    public int TcpPort { get; set; } = AppConstants.AlarmTcpPort;

    public bool Favorite { get; set; }

    /// <summary>
    /// Phase-1 leftover flag (FR-18 "wird benachrichtigt"): superseded by the
    /// per-profile <see cref="RecipientAssignment"/> mapping (Teil 2, Abschnitt 4) for
    /// anything alarm-related. Kept only so a Konfigurationsanzeige can still show
    /// "this device is known to be reachable" independent of any specific profile;
    /// AlarmSender no longer reads this field to decide alarm targets.
    /// </summary>
    public bool Notified { get; set; }

    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// True until the user has consciously set-or-left the Notified checkbox once (FR-25).
    /// Drives the "Neu" row highlight in the Individuell list (FR-24); no separate popup/snooze.
    /// </summary>
    public bool IsNew { get; set; } = true;

    public DateTimeOffset LastSeenUtc { get; set; }

    /// <summary>
    /// LAN-Verschlüsselung (siehe CLAUDE.md "Lizenz &amp; Secrets", SecureEnvelopeCodec):
    /// höchste Boot-Call-Protokollversion, die dieses Gerät bei DIREKTEM Kontakt gemeldet
    /// hat (nie aus Gossip übernommen - siehe DiscoveryService.HandleDatagramAsync, gleiches
    /// Prinzip wie AdminVerified). <c>null</c> = noch nie direkter Kontakt, oder ein alter,
    /// vor-verschlüsselungsfähiger Stand - beides führt zum selben Klartext-Fallback beim
    /// Senden (siehe PeerCryptoCapability).
    /// </summary>
    public int? ProtocolVersion { get; set; }

    /// <summary>
    /// Trust-on-First-Use-gepinnter Ed25519-Geräte-Identitätsschlüssel dieses Geräts,
    /// ausschließlich bei direktem Boot-Call-Kontakt gesetzt (siehe DiscoveryService).
    /// Meldet ein späterer direkter Kontakt für dieselbe DeviceId einen ANDEREN Schlüssel,
    /// wird der neue NICHT übernommen (mögliches Klon-/Kompromittierungs-Signal) - der Pin
    /// bleibt beim zuerst gesehenen Wert, der Vorfall landet im Audit-Log.
    /// </summary>
    public string? PinnedDeviceIdentityPublicKeyBase64 { get; set; }
}
