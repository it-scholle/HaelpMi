using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Lokale JSON-Ablage für <see cref="LicenseKeyPairEntry"/> (Issue #56) - ein Schlüsselpaar
/// pro Kundengruppe statt eines globalen, damit ein kompromittierter privater Schlüssel nur
/// eine einzige Kundengruppe betrifft. Gleiches Muster wie <see cref="LicenseRegistryStore"/>,
/// bewusst ohne Vaultwarden-Anbindung (analog zu <c>LicenseFileSigner</c>-Kommentar - eine
/// zentrale, verschlüsselte Ablage ist bei Bedarf ein eigenes Ticket, analog #42).
/// </summary>
internal static class LicenseKeyPairStore
{
    private static readonly string DefaultRegistryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaelpMi.InstallCreator", "lizenzschluesselpaare.json");

    /// <summary>Test-Hook (siehe HaelpMi.InstallCreator.Tests) - gleiches Prinzip wie HaelpMi.Core/Storage/AppPaths.UseRootForTests, hier nur eine einzelne Datei statt eines ganzen Wurzelordners.</summary>
    internal static string? RegistryFilePathOverride { private get; set; }

    private static string RegistryFilePath => RegistryFilePathOverride ?? DefaultRegistryFilePath;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static void Append(LicenseKeyPairEntry entry)
    {
        try
        {
            var entries = Load();
            entries.Add(entry);
            var directory = Path.GetDirectoryName(RegistryFilePath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(RegistryFilePath, JsonSerializer.Serialize(entries, SerializerOptions));
        }
        catch (IOException)
        {
            // best-effort, gleiche Begründung wie CustomerRegistryStore.Append
        }
    }

    public static List<LicenseKeyPairEntry> Load()
    {
        try
        {
            if (!File.Exists(RegistryFilePath))
            {
                return new List<LicenseKeyPairEntry>();
            }
            return JsonSerializer.Deserialize<List<LicenseKeyPairEntry>>(File.ReadAllText(RegistryFilePath)) ?? new List<LicenseKeyPairEntry>();
        }
        catch (IOException)
        {
            return new List<LicenseKeyPairEntry>();
        }
        catch (JsonException)
        {
            return new List<LicenseKeyPairEntry>();
        }
    }

    /// <summary>Für "Lizenz erstellen" - automatischer Lookup statt der früheren manuellen Schlüsseldatei-Auswahl.</summary>
    public static LicenseKeyPairEntry? Find(Guid customerGroupId) =>
        Load().FirstOrDefault(e => e.CustomerGroupId == customerGroupId);
}
