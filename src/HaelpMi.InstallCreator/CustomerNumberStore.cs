using System;
using System.IO;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Vergibt die nächste Kundennummer (Issue #39) - fortlaufend, beginnend bei 10001, rein
/// lokal auf dem Rechner des Ersteller (kein zentraler Server, siehe CLAUDE.md). Der Zähler
/// liegt als einzelne Zahl in einer Textdatei in %LocalAppData%, damit er über mehrere
/// Install-Creator-Läufe hinweg erhalten bleibt; das Feld im Hauptfenster bleibt trotzdem
/// editierbar, falls von Hand eine andere Nummer vergeben werden soll.
/// </summary>
internal static class CustomerNumberStore
{
    private const int FirstCustomerNumber = 10001;

    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaelpMi.InstallCreator", "next-customer-number.txt");

    public static int GetNextSuggested()
    {
        try
        {
            if (File.Exists(StateFilePath) && int.TryParse(File.ReadAllText(StateFilePath).Trim(), out var stored))
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

    public static void Advance(int usedCustomerNumber)
    {
        try
        {
            var directory = Path.GetDirectoryName(StateFilePath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(StateFilePath, (usedCustomerNumber + 1).ToString());
        }
        catch (IOException)
        {
            // best-effort - schlägt das Schreiben fehl, schlägt beim nächsten Start
            // höchstens wieder dieselbe Nummer vor, kein Datenverlust
        }
    }
}
