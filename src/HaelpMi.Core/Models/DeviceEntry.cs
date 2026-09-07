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
    /// Zeitpunkt, zu dem dieses Gerät erstmals irgendwo im Kreis gesehen wurde - anders als
    /// <see cref="LastSeenUtc"/> nie überschrieben, außer eine spätere Meldung (Gossip)
    /// belegt einen NOCH früheren Zeitpunkt (siehe DeviceStore.Upsert). Grundlage für
    /// <see cref="HaelpMi.Core.Licensing.LicenseLimitEvaluator"/> (Issue #59/#60): ohne zentrale Instanz
    /// braucht die Entscheidung "welche Geräte gehören zu den ersten N laut Lizenz" einen
    /// Wert, auf den sich alle Geräte unabhängig voneinander einigen können.
    /// </summary>
    public DateTimeOffset FirstSeenUtc { get; set; }

    /// <summary>
    /// Lokale, im Dashboard gesetzte Bestätigung "gesehen" für den Lizenzlimit-Hinweis
    /// (Issue #60) - sobald wahr, taucht dieses Gerät nicht mehr im Banner auf, auch wenn es
    /// weiterhin lizenzüberschritten ist. Analog zu <see cref="IsNew"/>, nur für diesen einen
    /// Hinweis statt der generellen "Neu"-Markierung.
    /// </summary>
    public bool LicenseLimitWarningAcknowledged { get; set; }

    /// <summary>
    /// Manuelle Admin-Entscheidung "aktivieren/deaktivieren" im Geräte-Tab des Dashboards
    /// (Issue #61) - siehe <see cref="LicenseOverride"/> und
    /// <see cref="HaelpMi.Core.Licensing.LicenseLimitEvaluator"/>, das diesen Wert auswertet.
    /// Anders als Favorite/Notified/Note ist das keine rein lokale Entscheidung: eine
    /// Deaktivierung soll auch für Peers gelten, die dasselbe Gerät kennen - siehe
    /// <see cref="LicenseOverrideSetAtUtc"/> für die Verbreitung. Ein Gerät setzt diesen
    /// Wert nie über sich selbst (nur ein Admin über ein ANDERES Gerät), deshalb fasst
    /// DeviceStore.Upsert ihn beim Selbstbericht des betroffenen Geräts nie an.
    /// </summary>
    public LicenseOverride LicenseOverride { get; set; }

    /// <summary>
    /// Zeitpunkt der letzten <see cref="LicenseOverride"/>-Entscheidung, null solange noch
    /// nie ein Admin dieses Gerät manuell aktiviert/deaktiviert hat. Ohne zentrale Instanz
    /// oder Admin-Signatur (siehe CLAUDE.md - diese Infrastruktur existiert auf diesem
    /// Versionsstand nicht) ist "neuester Zeitstempel gewinnt" die einzige Konfliktregel,
    /// falls zwei Admins gegensätzlich entscheiden: DeviceStore.Upsert übernimmt eine per
    /// Gossip gemeldete Fremdmeinung nur, wenn ihr Zeitstempel neuer ist als der lokal
    /// bekannte (Nutzer-Präferenz: Zeitstempel statt bloßem Bool für so einen Zustand).
    /// </summary>
    public DateTimeOffset? LicenseOverrideSetAtUtc { get; set; }
}
