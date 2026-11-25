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

    private static readonly HashSet<string> Abgeschlossen = [];

    /// <summary>Meldet der Gegenseite, dass der Test mit diesem Markenpräfix hier gescheitert ist; deren Warten auf "&lt;praefix&gt;-…" endet sofort.</summary>
    public static void Abbrechen(string praefix)
    {
        lock (Abgeschlossen)
        {
            if (Abgeschlossen.Contains(praefix))
            {
                return;
            }
        }

        Signal(AbbruchMarke(praefix));
    }

    /// <summary>
    /// Alle Marken dieses Tests von hier sind gesetzt. Ein späterer Fehlschlag (Abschluss-Assert) bricht die Gegenseite nicht
    /// mehr ab; sonst scheiterte ein drittes Gerät, das noch auf die letzte Marke des anderen Empfängers wartet, nur als Folgefehler.
    /// </summary>
    public static void Abschliessen(string praefix)
    {
        lock (Abgeschlossen)
        {
            Abgeschlossen.Add(praefix);
        }
    }

    /// <returns>Inhalt der Marke.</returns>
    public static async Task<string> WaitForAsync(string marke, TimeSpan timeout)
    {
        var pfad = PfadFuer(marke);
        var praefix = TestPraefix().Match(marke);
        var abbruch = praefix.Success ? PfadFuer(AbbruchMarke(praefix.Groups[1].Value)) : null;
        var frist = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < frist)
        {
            if (await LiesAsync(pfad) is { } inhalt)
            {
                return inhalt;
            }

            if (abbruch is not null && File.Exists(abbruch))
            {
                // Scheitert die Gegenseite erst nach ihrer letzten Marke (Abschluss-Assert), spiegelt das Relay beide
                // Marken im selben Durchlauf in beliebiger Reihenfolge; die schon gesetzte Marke gilt dann noch.
                await Task.Delay(AbbruchNachlauf);
                if (await LiesAsync(pfad) is { } spaet)
                {
                    return spaet;
                }

                throw new TimeoutException($"Rendezvous-Marke '{marke}': Test {praefix.Groups[1].Value} ist auf der Gegenseite gescheitert, siehe deren Ergebnis.");
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Rendezvous-Marke '{marke}' ist nach {timeout} nicht in {Ordner} erschienen.");
    }

    private static readonly TimeSpan AbbruchNachlauf = TimeSpan.FromSeconds(1);

    private static async Task<string?> LiesAsync(string pfad)
    {
        if (!File.Exists(pfad))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(pfad);
        }
        catch (IOException)
        {
            // Spiegel-Job schreibt gerade, nächster Durchlauf.
            return null;
        }
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
