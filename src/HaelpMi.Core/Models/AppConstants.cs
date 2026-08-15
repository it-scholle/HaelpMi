namespace HaelpMi.Core.Models;

/// <summary>
/// Fixed, shared network and storage constants. Changing the ports requires every
/// device on the network to update in lockstep, so these are not user-configurable.
/// </summary>
public static class AppConstants
{
    /// <summary>UDP port used for boot-call announce/reply (Phase 1 FR-21/FR-22, Teil 2 Abschnitt 9).</summary>
    public const int DiscoveryUdpPort = 51500;

    /// <summary>
    /// Aktuelle Boot-Call-Protokollversion (LAN-Verschlüsselung, siehe CLAUDE.md
    /// "Lizenz &amp; Secrets" und SecureEnvelopeCodec) - ein Gerät, das dies in seinem
    /// eigenen <c>BootCallMessage.ProtocolVersion</c> meldet, versteht SecureEnvelope
    /// (Ed25519-Geräte-Identität + gruppenweiter ChaCha20-Poly1305-Schlüssel). 1 war
    /// implizit jede Version vor Einführung dieses Felds (kein Envelope-Support, reines
    /// Klartextformat) - beginnt bewusst bei 2, nicht 1, damit "Feld fehlt" (alter Peer,
    /// deserialisiert als <c>null</c>) nie mit einer echten Versionsnummer verwechselt
    /// werden kann.
    /// </summary>
    public const int CurrentProtocolVersion = 2;

    /// <summary>
    /// Fest verdrahtete ID der eingebauten "Alle"-Gruppe (Nutzerwunsch 09.08.2026: "Default-
    /// Gruppe, die auch funktioniert, wenn der Admin gerade nicht online ist"). Jedes Gerät
    /// kennt diese ID unabhängig und ganz ohne Config-Sync (siehe <c>SharedConfigStore</c>) -
    /// ihre Mitgliedschaft wird nie aus gespeicherten/synchronisierten DeviceIds gelesen,
    /// sondern bei jeder Alarm-/Sender-Auflösung live aus den per Gossip (Boot-Call-Reply-
    /// Anhang, siehe DiscoveryService/KnownDeviceSummary) bekannten Geräten berechnet (siehe
    /// RecipientResolver). Deshalb braucht "Alle" selbst keine eigene Propagierung.
    /// </summary>
    public static readonly Guid AllDevicesGroupId = new("d3f1a000-a11d-4000-9000-000000000001");

    /// <summary>TCP port each device listens on for incoming alarms (FR-9) and their acks (FR-13).</summary>
    public const int AlarmTcpPort = 51501;

    /// <summary>UDP port for Config-Sync broadcast announces (Teil 2, Abschnitt 10) - "analog zum Boot-Call".</summary>
    public const int ConfigSyncUdpPort = 51502;

    /// <summary>TCP port for pulling the full <see cref="SharedConfig"/> after seeing a newer-version announce.</summary>
    public const int ConfigSyncTcpPort = 51503;

    /// <summary>TCP port for the exclusive edit-lock request/response call (Teil 2, Abschnitt 5).</summary>
    public const int EditLockTcpPort = 51504;

    /// <summary>TCP port for post-trigger alarm feedback ("bin unterwegs" + status relay, Teil 2, Abschnitt 7/8).</summary>
    public const int AlarmFeedbackTcpPort = 51505;

    /// <summary>Name of the local named-pipe used for Config/Admin-Dashboard &lt;-&gt; Agent IPC on the same machine.</summary>
    public const string IpcPipeName = "HaelpMi.Agent.Ipc";

    /// <summary>Name of the local named-pipe used for Agent &lt;-&gt; Update-Dienst IPC on the same machine (Teil 2, Abschnitt 11).</summary>
    public const string UpdateServiceIpcPipeName = "HaelpMi.UpdateService.Ipc";

    /// <summary>TCP port for pulling a signed update package (payload + Manifest) from a peer with a newer <see cref="LiveIdentity.ProgramVersion"/> (Teil 2, Abschnitt 11).</summary>
    public const int UpdatePackageTcpPort = 51506;

    /// <summary>TCP port for pushing not-yet-acknowledged <see cref="AuditLogEntry"/> batches to a reachable admin device (Nutzerwunsch 14.08.2026: revisionssicheres Audit-Log ohne zentrale Instanz, siehe AuditSyncService).</summary>
    public const int AuditSyncTcpPort = 51507;

    /// <summary>TCP port for the Admin&lt;-&gt;Admin Digest-/Mesh-Abgleich (Nutzerwunsch 15.08.2026: "bleeding edge" unter mehreren gleichzeitig erreichbaren Admins, siehe AuditSyncService.ReconcileWithAdminPeerAsync). Bewusst ein eigener Port statt Multiplexing über AuditSyncTcpPort - ein Port pro Nachrichtenzweck, wie überall sonst in diesem Projekt (Discovery/ConfigSync/EditLock/Update).</summary>
    public const int AuditMeshTcpPort = 51508;

    /// <summary>Max. <see cref="AuditLogEntry"/>-Einträge pro Push-/Mesh-Batch - analog <see cref="Storage.ConfigHistoryStore.MaxEntriesPerScope"/>: verhindert ein einzelnes überdimensioniertes Paket bei großem Rückstand, der wird dann über mehrere Trigger-Ereignisse verteilt nachgeliefert.</summary>
    public const int AuditSyncBatchCap = 200;

    /// <summary>Timeout pro Ziel-Peer für einen einzelnen AuditSync-Push/-Digest-Call (analog EditLockService.RequestTimeout, etwas großzügiger wegen der potenziell größeren Nutzlast).</summary>
    public static readonly TimeSpan AuditSyncRequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Folder under the machine-wide %ProgramData% where all local device state lives (Teil 2, FR-34 - siehe AppPaths.cs).</summary>
    public const string AppDataFolderName = "HaelpMi";

    /// <summary>How long a sender waits for a single target's ack before giving up on it (FR-14 count).</summary>
    public static readonly TimeSpan AlarmAckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Hotkey-triggered alarm repeat interval (Teil 2, Abschnitt 7: "wiederholtes Senden im 5-Sekunden-Takt").</summary>
    public static readonly TimeSpan AlarmRepeatInterval = TimeSpan.FromSeconds(5);

    /// <summary>Hard stop for a repeating alarm regardless of responses (Teil 2, Abschnitt 7).</summary>
    public static readonly TimeSpan AlarmMaxDuration = TimeSpan.FromMinutes(5);

    /// <summary>Receiver-side auto-close delay measured from the *last* received signal, not the first (Teil 2, Abschnitt 8).</summary>
    public static readonly TimeSpan AlarmAutoCloseAfterLastSignal = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Sender-side status banner (SenderStatusWindow): how long it stays visible after
    /// the alarm ends via threshold or the 5-minute timeout, before closing itself
    /// (Nutzerwunsch 09.08.2026 - genug Zeit, kurz die "Auf dem Weg"-Liste zu lesen, ohne
    /// dauerhaft manuell weggeklickt werden zu müssen). Gilt NICHT für ein manuelles
    /// Abbrechen - das schließt das Banner sofort, siehe SenderStatusWindow.
    /// </summary>
    public static readonly TimeSpan SenderStatusBannerAutoCloseAfterFinish = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Testmodus-Toggle im Konfigurator (Nutzerwunsch 13.08.2026): One-Shot, gilt für den
    /// nächsten Hotkey-Trigger und deaktiviert sich automatisch nach dieser Zeitspanne,
    /// falls bis dahin kein Hotkey gedrückt wurde - kein Dauerzustand, keine Persistierung.
    /// Siehe <see cref="TestModeArmState"/>: das Ablaufen ist reine Zeitstempel-Arithmetik,
    /// kein separater Reset-Pfad muss dafür erfolgreich laufen (Sicherheitsgarantie gegen
    /// einen liegen gelassenen Toggle).
    /// </summary>
    public static readonly TimeSpan TestModeTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Exclusive edit-lock auto-release after this much inactivity (Teil 2, Abschnitt 5).</summary>
    public static readonly TimeSpan EditLockInactivityTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Random backoff range on an edit-lock request collision (Teil 2, Abschnitt 5).</summary>
    public static readonly (int MinMs, int MaxMs) EditLockCollisionBackoff = (200, 800);

    /// <summary>Random delay before pulling a newly-observed update, spread across a device's own quota window (Teil 2, Abschnitt 11: "zufälliger Jitter vor dem Pull") - avoids every device in a circle hammering the same source peer/the network at once.</summary>
    public static readonly (int MinSeconds, int MaxSeconds) UpdatePullJitter = (5, 300);

    /// <summary>Kill-switch (Teil 2, Abschnitt 11): this many consecutive failed update attempts trigger the lockout below instead of retrying forever.</summary>
    public const int UpdateMaxConsecutiveFailures = 3;

    /// <summary>Lockout duration after the kill-switch trips - a device simply stops attempting updates until this elapses (Teil 2, Abschnitt 11: "24h-Lockout").</summary>
    public static readonly TimeSpan UpdateLockoutDuration = TimeSpan.FromHours(24);

    /// <summary>
    /// Top-level Dateinamen im Installationsverzeichnis, die vom Installer stammen (nicht
    /// aus dem generischen P2P-Update-Paket) und deshalb einen 0-Downtime-Swap
    /// (<c>UpdateServiceWorker.ConfirmSwapAsync</c>) unverändert überleben müssen - sie
    /// würden sonst beim "alte Version deinstallieren"-Schritt mitgelöscht, weil das neu
    /// gepullte Paket sie nie enthält (kundenspezifisch, nie über P2P verteilt). Bugfix
    /// 09.08.2026: <c>deployment.json</c> fehlte nach einem Swap komplett, die App
    /// startete danach gar nicht mehr (<see cref="AppPaths.DeploymentInfoFilePath"/> wirft
    /// beim Fehlen). <c>HaelpMi-User-Setup.exe</c> (Admin-Installation, siehe
    /// HaelpMiCommon.iss.inc) hätte denselben Fehler gehabt, nur unauffälliger
    /// ("Exportieren" schlägt erst beim nächsten Klick fehl statt beim Start).
    /// </summary>
    public static readonly string[] InstallerOwnedFileNames = { "deployment.json", "HaelpMi-User-Setup.exe" };

    /// <summary>
    /// Fest verdrahtete ScopeId für <see cref="EditScopeKind.UpdateRollout"/> - es gibt
    /// kundengruppenweit immer genau einen Update-Rollout-Datensatz (siehe
    /// <see cref="UpdateRolloutState"/>), anders als Gruppen/Alarm-Profile mit echten,
    /// eigenen Ids je Datensatz.
    /// </summary>
    public static readonly Guid UpdateRolloutScopeId = new("d3f1a000-a11d-4000-9000-000000000002");

    /// <summary>
    /// Fest verdrahtete ScopeId für <see cref="EditScopeKind.NetworkBridge"/> - gleiches
    /// Prinzip wie <see cref="UpdateRolloutScopeId"/>: genau ein Bridge-Seed-Datensatz
    /// kundengruppenweit (Multi-VLAN-Bootstrap, siehe <see cref="SharedConfig.BridgeSeedAddresses"/>).
    /// </summary>
    public static readonly Guid NetworkBridgeScopeId = new("d3f1a000-a11d-4000-9000-000000000003");

    /// <summary>
    /// Intervall der lokalen Lizenz-Neuprüfung im Agent (rein lokaler Dateizugriff, kein
    /// Netzwerkverkehr - das Heartbeat-/Polling-Verbot in CLAUDE.md bezieht sich nur auf
    /// Netzwerkverkehr und gilt hier nicht). Tagesgranularität der Eskalationsstufen (siehe
    /// Licensing.LicenseEvaluator) macht ein enges Intervall unnötig.
    /// </summary>
    public static readonly TimeSpan LicenseCheckInterval = TimeSpan.FromHours(6);
}
