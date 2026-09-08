using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Lokales Kundenregister (Issue #31/#21) - eine JSON-Datei mit einem Eintrag pro erstelltem
/// Admin-Installer, löst den bisherigen reinen Zähler aus #39 ab. Test-Installer landen seit
/// #43 in einer eigenen zweiten Datei mit eigener Nummernreihe (T0001+, siehe
/// <see cref="FormatDisplay"/>), damit Testbuilds nicht mehr die echte Kundennummer-Sequenz
/// aufblähen.
///
/// Bewusst (noch) ohne Vaultwarden-Anbindung: aktuell gibt es nur einen Installer-Rechner
/// (kein Kollisionsrisiko durch parallele Instanzen), und die frühere Vaultwarden/Bitwarden-
/// CLI-Anbindung (nur noch in BUILD-UND-INSTALLATION.md dokumentiert, nicht mehr im Code
/// vorhanden) brauchte für Entschlüsseln + Notiz-Lesen teils bis zu einer Minute - eine
/// zentrale, verschlüsselte Ablage ist als eigenes Ticket für release-2.0 vorgemerkt (#42).
/// </summary>
internal static class CustomerRegistryStore
{
    private const int FirstCustomerNumber = 10001;
    private const int FirstTestNumber = 1;

    private static readonly string BaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaelpMi.InstallCreator");

    private static readonly string DefaultRegistryFilePath = Path.Combine(BaseDirectory, "kundenregister.json");
    private static readonly string DefaultTestRegistryFilePath = Path.Combine(BaseDirectory, "test-kundenregister.json");

    /// <summary>Test-Hooks (siehe HaelpMi.InstallCreator.Tests) - gleiches Prinzip wie LicenseKeyPairStore.RegistryFilePathOverride.</summary>
    internal static string? RegistryFilePathOverride { private get; set; }
    internal static string? TestRegistryFilePathOverride { private get; set; }

    private static string RegistryFilePath => RegistryFilePathOverride ?? DefaultRegistryFilePath;
    private static string TestRegistryFilePath => TestRegistryFilePathOverride ?? DefaultTestRegistryFilePath;

    // Vorgänger-Datei aus #39 (reiner Zähler, keine Einträge) - nur noch für die einmalige
    // Migration gelesen, damit die Produktiv-Nummerierung beim ersten Start mit dem neuen
    // Register nicht wieder bei 10001 anfängt.
    private static readonly string LegacyCounterFilePath = Path.Combine(BaseDirectory, "next-customer-number.txt");

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// Menschenlesbare Anzeige einer Kundennummer - Testnummern bekommen das T-Präfix aus #43
    /// (reine Anzeige-/Registerkonvention; <see cref="CustomerRegistryEntry.Kundennummer"/>
    /// bleibt intern numerisch, siehe deployment.json-Format aus #39).
    /// </summary>
    public static string FormatDisplay(int kundennummer, bool isTestInstaller) =>
        isTestInstaller ? $"T{kundennummer:D4}" : kundennummer.ToString();

    public static int GetNextSuggested(bool isTestInstaller)
    {
        if (isTestInstaller)
        {
            var testEntries = Load(TestRegistryFilePath);
            return testEntries.Count > 0 ? testEntries.Max(e => e.Kundennummer) + 1 : FirstTestNumber;
        }

        var entries = Load(RegistryFilePath);
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

    public static void Append(bool isTestInstaller, CustomerRegistryEntry entry)
    {
        var path = isTestInstaller ? TestRegistryFilePath : RegistryFilePath;
        try
        {
            var entries = Load(path);
            entries.Add(entry);
            Directory.CreateDirectory(BaseDirectory);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, SerializerOptions));
        }
        catch (IOException)
        {
            // best-effort - schlägt das Schreiben fehl, schlägt beim nächsten Start höchstens
            // wieder dieselbe Nummer vor, kein Datenverlust am eigentlichen Installer
        }
    }

    public static List<CustomerRegistryEntry> Load(bool isTestInstaller) =>
        Load(isTestInstaller ? TestRegistryFilePath : RegistryFilePath);

    /// <summary>
    /// Bereits bekannte Kundennummer erneut eingegeben (z. B. Installer-Neubau für denselben
    /// Kunden) → dieselbe CustomerGroupId liefern statt einer neuen. Sonst würde ein Rebuild
    /// bereits ausgegebene Lizenzen entwerten, weil deren Signatur an die CustomerGroupId und
    /// das dazu gehörige Schlüsselpaar gebunden ist (siehe LicenseKeyPairStore).
    /// </summary>
    public static Guid ResolveCustomerGroupId(bool isTestInstaller, int kundennummer) =>
        Load(isTestInstaller).FirstOrDefault(e => e.Kundennummer == kundennummer)?.CustomerGroupId ?? Guid.NewGuid();

    private static List<CustomerRegistryEntry> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new List<CustomerRegistryEntry>();
            }
            return JsonSerializer.Deserialize<List<CustomerRegistryEntry>>(File.ReadAllText(path)) ?? new List<CustomerRegistryEntry>();
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
