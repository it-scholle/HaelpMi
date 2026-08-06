namespace HaelpMi.Core.Models;

/// <summary>
/// App-internal role (CLAUDE.md v2: "Admin-Konto vs. User-Konto ist eine App-interne
/// Rolle, im Lizenz-/Konfigfile verankert, unabhängig von Windows-eigenen
/// Adminrechten"). Comes from <see cref="DeploymentInfo"/> - baked in at install time by
/// which installer variant (Admin/User) was used - never something the running app
/// lets a user toggle.
/// </summary>
public enum Role
{
    User,
    Admin,
}
