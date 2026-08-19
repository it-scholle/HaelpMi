using System.Reflection;
using HaelpMi.Core.Interop;
using HaelpMi.Core.Models;

namespace HaelpMi.Core.Runtime;

/// <summary>
/// Assembles a <see cref="LiveIdentity"/> from the mix of sources network services need
/// but shouldn't each have to know about individually (Teil 2, Abschnitt 2/3): the
/// Windows computer name and currently logged-on user are read live every time, never
/// cached from disk, since "Benutzerkonto: dynamisch, immer der aktuell angemeldete
/// Windows-User" is a hard requirement, not just an initial-value convenience.
/// </summary>
public static class LiveIdentityFactory
{
    public static string CurrentProgramVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static LiveIdentity Create(OwnSettings settings, DeploymentInfo deployment) => new(
        deployment.CustomerGroupId,
        settings.DeviceId,
        Environment.MachineName,
        Environment.UserName,
        settings.RoomName,
        settings.RoomNumber,
        deployment.Role,
        RemoteSessionDetector.IsCurrentSessionRemote(),
        CurrentProgramVersion,
        settings.AppliedConfigVersion);
}
