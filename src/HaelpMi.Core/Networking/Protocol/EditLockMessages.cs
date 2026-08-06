using HaelpMi.Core.Models;

namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// TCP "will editieren"-Call (Teil 2, Abschnitt 5): sent by an Admin-Dashboard directly
/// to every other admin-capable device it knows about in the same Kunden-Gruppe, when
/// the admin selects a Gruppe or Alarm-Profil to edit. Not broadcast - addressed
/// connections, same pattern as the alarm channel. <see cref="ScopeKind"/>/
/// <see cref="ScopeId"/> identify exactly which record is being locked (war früher pro
/// Kreis, siehe EditScope.cs für den Hintergrund - "Kreis" bezeichnete von Anfang an die
/// kundennummernbasierte Netz-Isolation, nie ein Dashboard-Objekt).
/// </summary>
public sealed record EditLockRequestMessage(
    Guid CustomerGroupId,
    EditScopeKind ScopeKind,
    Guid ScopeId,
    Guid RequesterDeviceId,
    string RequesterComputerName,
    string RequesterUser,
    DateTimeOffset RequestedAtUtc);

/// <summary>
/// Response to an <see cref="EditLockRequestMessage"/>. <see cref="Granted"/> is false
/// whenever the responder itself currently holds (or is mid-negotiating) the lock for
/// that Gruppe/Alarm-Profil; <see cref="HolderComputerName"/>/<see cref="HolderUser"/>
/// are what the requester shows in its "XY administriert bereits"-style message.
/// </summary>
public sealed record EditLockResponseMessage(
    Guid CustomerGroupId,
    EditScopeKind ScopeKind,
    Guid ScopeId,
    bool Granted,
    string HolderComputerName,
    string HolderUser);
