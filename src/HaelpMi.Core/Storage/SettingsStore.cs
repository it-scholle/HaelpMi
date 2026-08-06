using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>
/// Loads/saves this device's own settings (FR-3, FR-36 bis FR-39). Die Ersteinrichtung
/// (Raum/Raumnummer/Geräte-ID) passiert seit Teil 2 ausschließlich im Installer, nicht
/// mehr in einem App-eigenen Wizard - ohne vollständige Pflichtangaben lässt der
/// Installer die Installation gar nicht erst abschließen. Eine fehlende oder ungültige
/// settings.json zur Laufzeit ist damit kein normaler Ersteinrichtungs-Zustand mehr,
/// sondern ein Zeichen einer unvollständigen/fehlerhaften Installation - genau wie bei
/// <see cref="DeploymentInfoStore"/> gibt es dafür keinen sinnvollen Fallback.
/// </summary>
public sealed class SettingsStore
{
    /// <summary>Throws if settings.json fehlt oder ungültig ist - siehe Klassenkommentar.</summary>
    public OwnSettings Load()
    {
        var settings = JsonFileStore.Load<OwnSettings>(AppPaths.SettingsFilePath);
        if (settings is null)
        {
            throw new InvalidOperationException(
                $"settings.json fehlt oder ist ungültig ({AppPaths.SettingsFilePath}). " +
                "Diese Installation wurde nicht korrekt über einen HälpMi-Installer erstellt - bitte neu installieren.");
        }

        return settings;
    }

    public void Save(OwnSettings settings) => JsonFileStore.Save(AppPaths.SettingsFilePath, settings);
}
