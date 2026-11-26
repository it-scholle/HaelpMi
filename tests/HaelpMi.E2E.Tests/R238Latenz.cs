using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using HaelpMi.Core.Storage;

namespace HaelpMi.E2E.Tests;

/// <summary>Gemeinsame Werte und Hilfen für T26 (Popup bedienbar und Quittung kurz nach dem Empfang), Testmethoden in GeraetATests.R238.cs und GeraetBTests.R238.cs.</summary>
internal static class R238Latenz
{
    /// <summary>Höher als in allen früheren Fällen (T23: 7000), sonst gilt die Config beim Abgleich als veraltet.</summary>
    public const int ConfigVersion = 7100;

    /// <summary>
    /// Höchstzeit des Agents vom Empfang bis „Popup sichtbar“ und bis „UI-Thread frei“ (Betriebs-Log, Uhr des Agents).
    /// Gemessen 0,32 bis 0,78 s; vor dem Fix hing der UI-Thread bis etwa 24 s.
    /// </summary>
    public static readonly TimeSpan MaxProdukt = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Höchstzeit vom Eintrag „alarm received“ bis das Alarmfenster per UIA gelesen und bedient werden kann. Enthält den
    /// ersten UIA-Aufbau im Agent (gemessen 2,8 bis 3,5 s in Teilläufen, 6,0 s in der vollen Suite bei 0,44 s
    /// Produktzeit); vor dem Fix bis etwa 24 s.
    /// </summary>
    public static readonly TimeSpan MaxBisBedienbar = TimeSpan.FromSeconds(10);

    /// <summary>So lange wartet B, bis die Zeitmessungen des Agents im Betriebs-Log stehen.</summary>
    public static readonly TimeSpan BetriebsLogFrist = TimeSpan.FromSeconds(5);

    /// <summary>So lange sucht B das Fenster; länger als der Fehler dauerte, damit die Messung auch im Fehlerfall einen Wert liefert.</summary>
    public static readonly TimeSpan Suche = TimeSpan.FromSeconds(40);

    /// <summary>Alarmfenster gefunden, Text lesbar, „Bin unterwegs“ aktiv. Jede UIA-Abfrage an ein hängendes Fenster läuft ins Zeitlimit.</summary>
    public static bool Bedienbar(Window popup)
    {
        try
        {
            var knopf = popup.FindFirstDescendant(cf => cf.ByAutomationId("OnMyWayButton"));
            return knopf is not null
                && knopf.IsEnabled
                && !string.IsNullOrEmpty(HaelpMiApp.NameVon(popup, "ThresholdStatusText"));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Schaltet das Betriebs-Log für den nächsten Agent-Start auf DEBUG, damit die Zeitmessungen im Empfangspfad
    /// mitgeschrieben werden. Liefert den bisherigen Inhalt (null: keine Datei) für <see cref="BetriebsLogZuruecksetzen"/>.
    /// </summary>
    public static string? BetriebsLogAufDebug()
    {
        var vorher = File.Exists(AppPaths.BetriebsLogEinstellungenPath) ? File.ReadAllText(AppPaths.BetriebsLogEinstellungenPath) : null;
        File.WriteAllText(AppPaths.BetriebsLogEinstellungenPath, "{\"Stufe\": \"Debug\"}");
        return vorher;
    }

    /// <summary>Stellt den Stand vor <see cref="BetriebsLogAufDebug"/> wieder her; wirkt ab dem nächsten Agent-Start.</summary>
    public static void BetriebsLogZuruecksetzen(string? vorher)
    {
        if (vorher is not null)
        {
            File.WriteAllText(AppPaths.BetriebsLogEinstellungenPath, vorher);
        }
        else if (File.Exists(AppPaths.BetriebsLogEinstellungenPath))
        {
            File.Delete(AppPaths.BetriebsLogEinstellungenPath);
        }
    }

    /// <summary>Zeilen der Module Alarmempfang und Vordergrund aus agent.log, als Beleg für die Laufzeiten im Empfangspfad.</summary>
    public static List<string> BetriebsLogZeilen()
    {
        var zeilen = new List<string>();
        foreach (var ordner in E2EUmgebung.LogOrdner())
        {
            var datei = Path.Combine(ordner, "agent.log");
            if (!File.Exists(datei))
            {
                continue;
            }

            try
            {
                using var stream = new FileStream(datei, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var leser = new StreamReader(stream);
                while (leser.ReadLine() is { } zeile)
                {
                    if (zeile.Contains("Alarmempfang", StringComparison.Ordinal) || zeile.Contains("Vordergrund", StringComparison.Ordinal))
                    {
                        zeilen.Add(zeile);
                    }
                }
            }
            catch (IOException)
            {
            }
        }

        return zeilen;
    }

    /// <summary>Zeitmessungen des Agents zum ersten Alarm ab <paramref name="seit"/>; null, solange ein Wert fehlt.</summary>
    /// <param name="zeilen">Zeilen aus <see cref="BetriebsLogZeilen"/>; eine Wiederholungswelle derselben Vorgangs-ID wird übergangen.</param>
    public static (TimeSpan Sichtbar, TimeSpan Frei)? ProduktZeiten(IEnumerable<string> zeilen, DateTimeOffset seit)
    {
        string? vorgang = null;
        TimeSpan? sichtbar = null;
        TimeSpan? frei = null;
        foreach (var zeile in zeilen)
        {
            if (!LeseEintrag(zeile, out var zeit, out var meldung, out var id) || zeit < seit)
            {
                continue;
            }

            if (vorgang is null && meldung == "Alarm empfangen.")
            {
                vorgang = id;
            }
            else if (vorgang is not null && id == vorgang && MsNachEmpfang.Match(meldung) is { Success: true } treffer)
            {
                var dauer = TimeSpan.FromMilliseconds(int.Parse(treffer.Groups[2].Value, CultureInfo.InvariantCulture));
                if (treffer.Groups[1].Value == "Popup sichtbar")
                {
                    sichtbar ??= dauer;
                }
                else
                {
                    frei ??= dauer;
                }
            }
        }

        return sichtbar is { } s && frei is { } f ? (s, f) : null;
    }

    private static readonly Regex MsNachEmpfang = new(@"^(Popup sichtbar|UI-Thread frei), (\d+) ms nach dem Empfang\.$", RegexOptions.CultureInvariant);

    private static bool LeseEintrag(string zeile, out DateTimeOffset zeit, out string meldung, out string? vorgang)
    {
        zeit = default;
        meldung = "";
        vorgang = null;
        try
        {
            using var json = JsonDocument.Parse(zeile);
            var wurzel = json.RootElement;
            if (!wurzel.TryGetProperty("zeit", out var z) || !wurzel.TryGetProperty("meldung", out var m))
            {
                return false;
            }

            zeit = DateTimeOffset.Parse(z.GetString()!, CultureInfo.InvariantCulture);
            meldung = m.GetString() ?? "";
            vorgang = wurzel.TryGetProperty("vorgang", out var v) ? v.GetString() : null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    public static string Sekunden(TimeSpan dauer) => dauer.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";

    public static void Sichere(string datei, IEnumerable<string> zeilen)
    {
        Directory.CreateDirectory(Artefakte.Ordner);
        File.AppendAllLines(Path.Combine(Artefakte.Ordner, datei), zeilen);
    }
}
