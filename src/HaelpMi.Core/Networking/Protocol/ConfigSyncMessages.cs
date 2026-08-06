namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// UDP broadcast announce for a config change (Teil 2, Abschnitt 10 - "analog zum
/// Boot-Call"). Carries only the new version number, not the payload itself - a
/// receiver whose own <see cref="OriginDeviceId"/>... whose own applied version is
/// lower connects directly back to <see cref="OriginDeviceId"/> to pull the full
/// <see cref="Models.SharedConfig"/> over TCP (<see cref="ConfigSyncPullRequestMessage"/>),
/// keeping the broadcast itself small regardless of how large the shared config grows.
/// </summary>
public sealed record ConfigSyncAnnounceMessage(
    Guid CustomerGroupId,
    Guid OriginDeviceId,
    int ConfigVersion,
    DateTimeOffset SentAtUtc);

/// <summary>TCP pull request sent to <see cref="ConfigSyncAnnounceMessage.OriginDeviceId"/> after seeing a newer announce.</summary>
public sealed record ConfigSyncPullRequestMessage(Guid CustomerGroupId, Guid RequesterDeviceId);

/// <summary>Response to a pull request: the full current <see cref="Models.SharedConfig"/>, serialized inline.</summary>
public sealed record ConfigSyncPullResponseMessage(Guid CustomerGroupId, Models.SharedConfig Config);
