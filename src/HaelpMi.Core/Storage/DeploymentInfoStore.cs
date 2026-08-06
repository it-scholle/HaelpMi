using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>
/// Reads the installer-provided <see cref="DeploymentInfo"/> (see
/// <see cref="AppPaths.DeploymentInfoFilePath"/>). Read-only from the running app's
/// point of view - nothing in HälpMi ever writes this file, only the installer does.
/// </summary>
public static class DeploymentInfoStore
{
    /// <summary>
    /// Throws if the file is missing/unreadable - a HälpMi executable running without a
    /// deployment.json next to it is a build/packaging bug, not a recoverable runtime
    /// state (there is no sensible default customer group id or role to fall back to).
    /// </summary>
    public static DeploymentInfo Load()
    {
        var info = JsonFileStore.Load<DeploymentInfo>(AppPaths.DeploymentInfoFilePath);
        if (info is null)
        {
            throw new InvalidOperationException(
                $"deployment.json fehlt oder ist ungültig ({AppPaths.DeploymentInfoFilePath}). " +
                "Diese Installation wurde nicht korrekt über einen HälpMi-Installer erstellt.");
        }

        return info;
    }
}
