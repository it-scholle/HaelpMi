using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>
/// Resolves where local device state lives. Machine-wide under
/// %ProgramData%\HaelpMi, not %AppData% (Teil 2, FR-34: Machine-Scope-Installation für
/// alle Windows-Konten eines Geräts). Eine Geräte-ID/ein Raum gehört zum physischen
/// Gerät, nicht zum jeweils angemeldeten Windows-Konto (FR-3/FR-38) - läge das unter
/// %AppData%, bekäme jedes Windows-Konto auf demselben PC seine eigene, neue Geräte-ID.
/// Der Installer legt den Ordner an und vergibt Schreibrechte für normale Nutzerkonten
/// (users-modify), da die App selbst rechtelos im User-Kontext läuft (CLAUDE.md).
/// </summary>
public static class AppPaths
{
    public static string RootFolder => _rootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        AppConstants.AppDataFolderName);

    private static string? _rootOverride;

    /// <summary>Test-only hook to redirect all storage to a temp folder instead of the real %AppData%.</summary>
    public static IDisposable UseRootForTests(string folder)
    {
        var previous = _rootOverride;
        _rootOverride = folder;
        Directory.CreateDirectory(folder);
        return new RestoreOnDispose(() => _rootOverride = previous);
    }

    public static string SettingsFilePath => Path.Combine(RootFolder, "settings.json");

    public static string DevicesFilePath => Path.Combine(RootFolder, "devices.json");

    /// <summary>
    /// Geräte-Identitätsschlüsselpaar (Ed25519, siehe Security.DeviceIdentityStore) - der
    /// private Schlüssel darin ist DPAPI-LocalMachine-geschützt, nicht CurrentUser (siehe
    /// dortige Klassendoku). Bewusst unter RootFolder wie settings.json/devices.json, nicht
    /// AppContext.BaseDirectory wie deployment.json: das ist Geräte-Laufzeitzustand, kein
    /// vom Installer verbindlich vorgegebener Wert.
    /// </summary>
    public static string DeviceIdentityFilePath => Path.Combine(RootFolder, "device-identity.json");

    public static string SharedConfigFilePath => Path.Combine(RootFolder, "shared-config.json");

    public static string ConfigHistoryFilePath => Path.Combine(RootFolder, "config-history.json");

    /// <summary>
    /// Anders als deployment.json bewusst unter RootFolder statt AppContext.BaseDirectory:
    /// eine Lizenz muss bei Verlängerung ohne Neuinstallation ersetzbar sein - ein Admin
    /// legt einfach eine neue license.json hierher, statt neu zu installieren.
    /// </summary>
    public static string LicenseFilePath => Path.Combine(RootFolder, "license.json");

    /// <summary>
    /// Written by the installer (from the Install-Creator's payload) into the install
    /// directory, not %AppData% - it describes the build, not this user's runtime state,
    /// and must survive independently of any %AppData% cleanup (Teil 2, Abschnitt 1/6).
    /// </summary>
    public static string DeploymentInfoFilePath => Path.Combine(AppContext.BaseDirectory, "deployment.json");

    public static void EnsureRootExists() => Directory.CreateDirectory(RootFolder);

    private sealed class RestoreOnDispose(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
