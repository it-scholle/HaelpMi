using System.Reflection;
using HaelpMi.Core.Interop;
using HaelpMi.Core.Models;
using HaelpMi.Core.Security;

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

    public static LiveIdentity Create(OwnSettings settings, DeploymentInfo deployment)
    {
        // Admin-Rollen-Kryptoverifikation, Migrationspfad (Nutzerwunsch 17.08.2026): ein
        // Admin-Gerät ohne jeden Schlüssel (weder Installer noch Trust-Store) erzeugt sich
        // hier beim ersten Aufruf selbst eines - danach ist dieser Aufruf ein billiger
        // Load-Only-Check (gleiches Idempotenz-Muster wie DeviceIdentityStore.LoadOrCreate,
        // das an vielen Stellen ebenfalls bei jedem ausgehenden Paket erneut aufgerufen
        // wird). Ein Installer-Schlüssel wird dabei nie ersetzt.
        AdminRoleTrustStore.EnsureSelfGeneratedKeyIfNeeded(deployment.Role, deployment.AdminRolePrivateKeyBase64);
        var trust = AdminRoleTrustStore.Load();
        var effectiveAdminRolePublicKey = deployment.AdminRolePublicKeyBase64 ?? trust.PinnedGroupPublicKeyBase64;

        return new(
            deployment.CustomerGroupId,
            settings.DeviceId,
            Environment.MachineName,
            Environment.UserName,
            settings.RoomName,
            settings.RoomNumber,
            deployment.Role,
            RemoteSessionDetector.IsCurrentSessionRemote(),
            CurrentProgramVersion,
            settings.AppliedConfigVersion,
            effectiveAdminRolePublicKey);
    }
}
