using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Lokales Kundenregister (Issue #31/#21) - eine JSON-Datei mit einem Eintrag pro erstelltem
/// Admin-Installer, löst den bisherigen reinen Zähler aus #39 ab. Bewusst (noch) ohne
/// Vaultwarden-Anbindung: aktuell gibt es nur einen Installer-Rechner (kein Kollisionsrisiko
/// durch parallele Instanzen), und die frühere Vaultwarden/Bitwarden-CLI-Anbindung (nur noch in
/// BUILD-UND-INSTALLATION.md dokumentiert, nicht mehr im Code vorhanden) brauchte für
/// Entschlüsseln + Notiz-Lesen teils bis zu einer Minute - eine zentrale, verschlüsselte Ablage
/// ist als eigenes Ticket für release-2.0 vorgemerkt statt hier mitgebaut.
/// </summary>
internal static class CustomerRegistryStore
{
    private const int FirstCustomerNumber = 10001;

    private static readonly string RegistryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaelpMi.InstallCreator", "kundenregister.json");

    // Vorgänger-Datei aus #39 (reiner Zähler, keine Einträge) - nur noch für die einmalige
    // Migration gelesen, damit die Nummerierung beim ersten Start mit dem neuen Register nicht
    // wieder bei 10001 anfängt.
    private static readonly string LegacyCounterFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaelpMi.InstallCreator", "next-customer-number.txt");

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static int GetNextSuggested()
    {
        var entries = Load();
        if (entries.Count > 0)
        {
            return entries.Max(e => e.Kundennummer) + 1;
        }

        try
        {
            if (File.Exists(LegacyCounterFilePath) && int.TryParse(File.ReadAllText(LegacyCounterFilePath).Trim(), out var stored))
            {
                return stored;
            }
        }
        catch (IOException)
        {
            // best-effort - fällt auf den Startwert zurück, der Nutzer sieht/korrigiert die
            // Nummer im Feld ohnehin vor dem Bauen
        }

        return FirstCustomerNumber;
    }

    public static void Append(CustomerRegistryEntry entry)
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
            // best-effort - schlägt das Schreiben fehl, schlägt beim nächsten Start höchstens
            // wieder dieselbe Nummer vor, kein Datenverlust am eigentlichen Installer
        }
    }

    public static List<CustomerRegistryEntry> Load()
    {
        try
        {
            if (!File.Exists(RegistryFilePath))
            {
                return new List<CustomerRegistryEntry>();
            }
            return JsonSerializer.Deserialize<List<CustomerRegistryEntry>>(File.ReadAllText(RegistryFilePath)) ?? new List<CustomerRegistryEntry>();
        }
        catch (IOException)
        {
            return new List<CustomerRegistryEntry>();
        }
        catch (JsonException)
        {
            return new List<CustomerRegistryEntry>();
        }
    }
}
