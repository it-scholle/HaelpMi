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
/// </summary>
/// <param name="AdminRolePublicKeyBase64">
/// Admin-Rollen-Kryptoverifikation (Nutzerwunsch 17.08.2026): der öffentliche Schlüssel,
/// mit dem JEDES Gerät (Admin wie User) fremde Admin-Behauptungen prüft - aufgelöst aus
/// <see cref="Models.DeploymentInfo.AdminRolePublicKeyBase64"/> (Neuinstallation) oder,
/// falls das fehlt, aus dem TOFU-gepinnten Wert in Security.AdminRoleTrustStore
/// (Migrationspfad). Der PRIVATE Schlüssel wandert bewusst NICHT hierher (LiveIdentity
/// fließt an viele Stellen, landet potenziell in Audit-Log-Strings) - Signieren liest ihn
/// gezielt über einen eigenen Provider, siehe DiscoveryService.
/// </param>
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
    string? AdminRolePublicKeyBase64 = null);
