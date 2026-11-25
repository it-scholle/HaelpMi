using System.Globalization;

namespace HaelpMi.E2E.Tests;

/// <summary>Gemeinsame Werte und Auswertung für T20 (Wiederholungstakt ab dem ersten Alarm), Testmethoden in GeraetATests.R177.cs und GeraetBTests.R177.cs.</summary>
internal static class R177Takt
{
    /// <summary>Höher als in allen früheren Fällen (T13: 6000), sonst gilt die Config beim Abgleich als veraltet.</summary>
    public const int ConfigVersion = 6100;

    /// <summary>5 s Takt plus Spielraum für Verbindungsaufbau und Log-Schreiben; der Fehler zeigte 6,3 bis 15 s.</summary>
    public static readonly TimeSpan MaxAbstand = TimeSpan.FromSeconds(6);

    /// <summary>Wie lange A nach dem Beenden des Agents auf B die Wellen beobachtet.</summary>
    public static readonly TimeSpan Beobachtung = TimeSpan.FromSeconds(20);

    /// <summary>Höchstdauer einer UI-Automation-Abfrage am frisch geöffneten Alarmfenster.</summary>
    public static readonly TimeSpan MaxUiaAntwort = TimeSpan.FromSeconds(2);

    /// <summary>Zeitpunkte der audit.log-Zeilen, die <paramref name="marke"/> enthalten.</summary>
    public static List<DateTimeOffset> Zeitpunkte(DateTimeOffset seit, string marke) =>
        HaelpMiApp.AuditLogZeilen(seit)
            .Where(z => z.Contains(marke, StringComparison.OrdinalIgnoreCase))
            .Select(z => DateTimeOffset.Parse(z[..z.IndexOf('\t')], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .OrderBy(t => t)
            .ToList();

    public static List<TimeSpan> Abstaende(IReadOnlyList<DateTimeOffset> zeitpunkte) =>
        zeitpunkte.Zip(zeitpunkte.Skip(1), (x, y) => y - x).ToList();

    public static string Text(IEnumerable<TimeSpan> abstaende) =>
        string.Join(", ", abstaende.Select(a => a.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s"));

    public static void Sichere(string datei, string zeile)
    {
        Directory.CreateDirectory(Artefakte.Ordner);
        File.AppendAllText(Path.Combine(Artefakte.Ordner, datei), zeile + Environment.NewLine);
    }
}
