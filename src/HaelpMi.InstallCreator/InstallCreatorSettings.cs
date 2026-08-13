using System.IO;
using System.Text.Json;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Lokal gemerkte, NICHT geheime Einstellungen für Install-Creator selbst (Vaultwarden-
/// Server-URL + Konto-E-Mail) - bewusst getrennt von allem, was HälpMi-Geräte betrifft
/// (%ProgramData%\HaelpMi gehört der eigentlichen App, nicht diesem Entwickler-Werkzeug).
/// Weder Master-Passwort noch der Update-Schlüssel selbst landen hier - beide bleiben
/// ausschließlich im Arbeitsspeicher für die Dauer eines Programmlaufs.
/// </summary>
public sealed class InstallCreatorSettings
{
    public string VaultwardenServerUrl { get; set; } = string.Empty;
    public string VaultwardenEmail { get; set; } = string.Empty;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaelpMi-InstallCreator", "settings.json");

    public static InstallCreatorSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<InstallCreatorSettings>(File.ReadAllText(FilePath));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // beschädigte/fehlende Datei - verhält sich wie "noch nie gespeichert"
        }

        return new InstallCreatorSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // best-effort - beim nächsten Start muss man Server/E-Mail halt erneut eintragen
        }
    }
}
