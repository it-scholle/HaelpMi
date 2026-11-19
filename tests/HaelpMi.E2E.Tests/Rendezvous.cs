using System.Text.RegularExpressions;

namespace HaelpMi.E2E.Tests;

/// <summary>
/// Treffpunkt zwischen den Testteilen auf A und B über Markendateien unter
/// &lt;Wurzel&gt;\rv\&lt;marke&gt;. Die Tests sehen nur den lokalen Ordner; der Orchestrator
/// spiegelt neue Marken zwischen den VMs.
/// </summary>
internal static partial class Rendezvous
{
    public static string Ordner => Path.Combine(E2EUmgebung.Wurzel, "rv");

    /// <param name="marke">Dateiname der Marke, nur Buchstaben, Ziffern, '.', '-' und '_' (z. B. "b-bereit").</param>
    /// <param name="inhalt">Optionaler Text für die Gegenseite, z. B. eine DeviceId.</param>
    public static void Signal(string marke, string inhalt = "")
    {
        var ziel = PfadFuer(marke);
        Directory.CreateDirectory(Ordner);

        // Erst vollständig schreiben, dann umbenennen: der Spiegel-Job soll nie eine halbe Datei sehen.
        var temp = Path.Combine(Ordner, $".{marke}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, inhalt);
        File.Move(temp, ziel, overwrite: true);
    }

    /// <summary>Meldet der Gegenseite, dass der Test mit diesem Markenpräfix hier gescheitert ist; deren Warten auf "&lt;praefix&gt;-…" endet sofort.</summary>
    public static void Abbrechen(string praefix) => Signal(AbbruchMarke(praefix));

    /// <returns>Inhalt der Marke.</returns>
    public static async Task<string> WaitForAsync(string marke, TimeSpan timeout)
    {
        var pfad = PfadFuer(marke);
        var praefix = TestPraefix().Match(marke);
        var abbruch = praefix.Success ? PfadFuer(AbbruchMarke(praefix.Groups[1].Value)) : null;
        var frist = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < frist)
        {
            if (abbruch is not null && File.Exists(abbruch))
            {
                throw new TimeoutException($"Rendezvous-Marke '{marke}': Test {praefix.Groups[1].Value} ist auf der Gegenseite gescheitert, siehe deren Ergebnis.");
            }

            if (File.Exists(pfad))
            {
                try
                {
                    return await File.ReadAllTextAsync(pfad);
                }
                catch (IOException)
                {
                    // Spiegel-Job schreibt gerade, nächster Durchlauf.
                }
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Rendezvous-Marke '{marke}' ist nach {timeout} nicht in {Ordner} erschienen.");
    }

    private static string PfadFuer(string marke)
    {
        if (!GueltigeMarke().IsMatch(marke))
        {
            throw new ArgumentException($"Ungültiger Markenname '{marke}'.", nameof(marke));
        }

        return Path.Combine(Ordner, marke);
    }

    private static string AbbruchMarke(string praefix) => $"{praefix}-abbruch";

    [GeneratedRegex("^(t[0-9]+)-")]
    private static partial Regex TestPraefix();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex GueltigeMarke();
}
