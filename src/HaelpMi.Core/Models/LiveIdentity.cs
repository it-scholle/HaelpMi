namespace HaelpMi.Core.Models;

/// <summary>
/// A fully-assembled "who am I right now" snapshot - the fields every outgoing network
/// message needs, gathered from a mix of sources (persisted <see cref="OwnSettings"/>,
/// the live Windows session, the running assembly's version). Network services
/// (boot-call, config-sync, edit-lock, alarm send) take a
/// <c>Func&lt;LiveIdentity&gt;</c> rather than reaching into OwnSettings/Win32 APIs
/// themselves, so they stay agnostic of exactly how "current user"/"is this RDP"/
/// "program version" are derived - that assembly work happens once, in the hosting app
/// (HaelpMi.Agent), not scattered across every Core service that needs to announce itself.
///
/// <see cref="FirstSeenUtc"/> siehe <see cref="OwnSettings.FirstSeenUtc"/> - Grundlage für
/// <see cref="HaelpMi.Core.Licensing.LicenseLimitEvaluator"/> (Issue #59/#60).
///
/// <see cref="LicenseOverride"/>/<see cref="LicenseOverrideSetAtUtc"/> siehe
/// <see cref="OwnSettings.LicenseOverride"/> (Issue #61-Nachtrag) - Default
/// <see cref="Models.LicenseOverride.None"/>/null, damit bestehende Aufrufer (Tests) ohne
/// Kenntnis dieser Felder unverändert kompilieren.
/// </summary>
public sealed record LiveIdentity(
    Guid CustomerGroupId,
    Guid DeviceId,
    string ComputerName,
    string User,
    string RoomName,
    string RoomNumber,
    Role Role,
    bool IsRemoteSession,
    string ProgramVersion,
    int ConfigVersion,
    DateTimeOffset FirstSeenUtc,
    LicenseOverride LicenseOverride = LicenseOverride.None,
    DateTimeOffset? LicenseOverrideSetAtUtc = null);
