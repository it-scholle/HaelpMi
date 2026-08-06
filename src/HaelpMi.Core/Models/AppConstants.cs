namespace HaelpMi.Core.Models;

/// <summary>
/// Fixed, shared network and storage constants. Changing the ports requires every
/// device on the network to update in lockstep, so these are not user-configurable.
/// </summary>
public static class AppConstants
{
    /// <summary>UDP port used for boot-call announce/reply (Phase 1 FR-21/FR-22, Teil 2 Abschnitt 9).</summary>
    public const int DiscoveryUdpPort = 51500;

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

    /// <summary>Folder under the machine-wide %ProgramData% where all local device state lives (Teil 2, FR-34 - siehe AppPaths.cs).</summary>
    public const string AppDataFolderName = "HaelpMi";

    /// <summary>How long a sender waits for a single target's ack before giving up on it (FR-14 count).</summary>
    public static readonly TimeSpan AlarmAckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Hotkey-triggered alarm repeat interval (Teil 2, Abschnitt 7: "wiederholtes Senden im 5-Sekunden-Takt").</summary>
    public static readonly TimeSpan AlarmRepeatInterval = TimeSpan.FromSeconds(5);

    /// <summary>Hard stop for a repeating alarm regardless of responses (Teil 2, Abschnitt 7).</summary>
    public static readonly TimeSpan AlarmMaxDuration = TimeSpan.FromMinutes(5);

    /// <summary>Stop condition: this many distinct "bin unterwegs" responses ends the repeat loop (Teil 2, Abschnitt 7).</summary>
    public const int AlarmAutoStopResponseCount = 2;

    /// <summary>Receiver-side auto-close delay measured from the *last* received signal, not the first (Teil 2, Abschnitt 8).</summary>
    public static readonly TimeSpan AlarmAutoCloseAfterLastSignal = TimeSpan.FromMinutes(1);

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
}
