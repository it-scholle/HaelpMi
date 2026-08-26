using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Lokale JSON-Ablage für <see cref="LicenseRegistryEntry"/> (Vorarbeit zu #18/#19), getrennt
/// vom Kundenregister (#31/#21) - eine Lizenz ist ein eigener, an eine Kundengruppe gebundener
/// Datensatz, keine Eigenschaft eines Installer-Baus. Noch nirgends verdrahtet: weder Erzeugung
/// (samt Ed25519-Signatur) noch Prüfung beim Kunden gehören zu diesem Ticket.
/// </summary>
internal static class LicenseRegistryStore
{
    private static readonly string RegistryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaelpMi.InstallCreator", "lizenzregister.json");

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static void Append(LicenseRegistryEntry entry)
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

    public static List<LicenseRegistryEntry> Load()
    {
        try
        {
            if (!File.Exists(RegistryFilePath))
            {
                return new List<LicenseRegistryEntry>();
            }
            return JsonSerializer.Deserialize<List<LicenseRegistryEntry>>(File.ReadAllText(RegistryFilePath)) ?? new List<LicenseRegistryEntry>();
        }
        catch (IOException)
        {
            return new List<LicenseRegistryEntry>();
        }
        catch (JsonException)
        {
            return new List<LicenseRegistryEntry>();
        }
    }
}
