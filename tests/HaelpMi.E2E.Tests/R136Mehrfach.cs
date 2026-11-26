using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using Xunit;
using Xunit.Abstractions;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.R136Messung;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

/// <summary>
/// T19 "Mehrfach-Nachwelle" auf Empfängerseite (B und C): Nach "Bin unterwegs" und "Schließen" schickt A noch
/// <see cref="MinWellen"/> bis <see cref="MaxWellen"/> Wellen derselben Session. Gezählt wird pro Zyklus die Zahl
/// der verschiedenen Alarmfenster (Fenster-Handles) ab dem ersten Popup bis zum Ende der Beobachtung (erwartet 1)
/// und ob "Bin unterwegs" danach wieder aktiv war (Rücksetzung). Nutzt nur Schnittstellen, die es schon in v0.46.1
/// gibt (Kind-Elemente der Fenster, "alarm received" im audit.log).
/// </summary>
internal static class R136Mehrfach
{
    /// <summary>Wert des zweiten Traits "Teil"; der erste bleibt <see cref="DreiGeraete.Teil"/>, damit ein Lauf ohne C den Test herausfiltert.</summary>
    public const string Teil = "MehrfachNachwelle";

    public const int MinWellen = 3;
    public const int MaxWellen = 5;
    public const int MinAbstandMs = 500;
    public const int MaxAbstandMs = 2000;

    /// <summary>Beobachtung nach der letzten Nachwelle; ein neues Fenster stand in v0.46.1 nach wenigen hundert ms.</summary>
    public static readonly TimeSpan Nachlauf = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan FreiTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SchliessenTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Takt = TimeSpan.FromMilliseconds(100);

    /// <summary>Wellenzahl und Abstände für Zyklus <paramref name="n"/>; zufällig, aber pro Zyklus reproduzierbar.</summary>
    public static (int Anzahl, int[] AbstaendeMs) Wellenplan(int n)
    {
        var zufall = new Random(n);
        var anzahl = zufall.Next(MinWellen, MaxWellen + 1);
        return (anzahl, Enumerable.Range(0, anzahl).Select(_ => zufall.Next(MinAbstandMs, MaxAbstandMs + 1)).ToArray());
    }

    /// <param name="geraet">"b" oder "c"; Teil der Marken und Dateinamen.</param>
    /// <param name="klickmuster">
    /// true: Erscheint während der Beobachtung ein Alarmfenster mit aktivem "Bin unterwegs", klickt das Gerät sofort
    /// wieder "Bin unterwegs" und "Schließen", sobald frei (Verhalten beim Kunden). false: nur zählen.
    /// </param>
    public static async Task EmpfaengerAsync(string geraet, bool klickmuster, ITestOutputHelper ausgabe)
    {
        var (a, _, _) = await DreiGeraete.BereiteAlarmprofilVorAsync(DreiGeraete.ConfigVersionMehrfach);
        await WarteAufGeraetAsync(a, DiscoveryTimeout);
        Rendezvous.Signal($"t19-{geraet}-bereit");

        using var app = new HaelpMiApp();
        var zyklen = E2EUmgebung.Zyklen;
        var version = AgentVersion();
        var csv = Path.Combine(Artefakte.Ordner, $"r136-mehrfach-{geraet}-{version}.csv");
        Directory.CreateDirectory(Artefakte.Ordner);
        File.WriteAllText(csv, MehrfachZeile.Kopf + Environment.NewLine, Encoding.UTF8);
        var zusammenfassungPfad = Path.Combine(Artefakte.Ordner, $"r136-mehrfach-{geraet}-zusammenfassung.json");

        var zeilen = new List<MehrfachZeile>();
        for (var n = 1; n <= zyklen; n++)
        {
            await Rendezvous.WaitForAsync($"t19-{n}-aus", n == 1 ? GegenseiteTimeout : SchrittTimeout);
            var zeile = await ZyklusAsync(app, a, geraet, n, klickmuster);
            zeilen.Add(zeile);
            File.AppendAllText(csv, zeile.Csv() + Environment.NewLine, Encoding.UTF8);
            ausgabe.WriteLine($"Zyklus {n}: {zeile.Kurz()}");

            // Nach jedem Zyklus, damit sie auch bei einem Abbruch im Artefakt liegt.
            File.WriteAllText(zusammenfassungPfad, Zusammenfassung(version, zyklen, geraet, klickmuster, zeilen), Encoding.UTF8);
            Rendezvous.Signal($"t19-{n}-{geraet}-fertig");
        }

        Rendezvous.Abschliessen("t19");
        ausgabe.WriteLine(File.ReadAllText(zusammenfassungPfad));

        var auswertbar = zeilen.Where(z => !z.Uebersprungen).ToList();
        var treffer = auswertbar.Count(z => z.Treffer);
        var haengend = auswertbar.Count(z => z.Haengt);
        var ohneNachwelle = auswertbar.Count(z => z.WellenEmpfangen == 0);
        Assert.True(treffer == 0 && haengend == 0,
            $"[T19F] Regression #136 (Mehrfach-Nachwelle) auf {geraet.ToUpperInvariant()}: in {treffer} von {auswertbar.Count} Zyklen mehr als ein Alarmfenster " +
            $"oder 'Bin unterwegs' wieder aktiv, in {haengend} Zyklen Schließen nie frei oder Fenster blieb offen (Agent {version}). " +
            $"Details: {Path.GetFileName(csv)} und r136-mehrfach-{geraet}-zusammenfassung.json im Artefakt.");

        // Sonst wäre ein grüner Lauf ohne eine einzige angekommene Nachwelle möglich.
        Assert.True(auswertbar.Count > 0 && ohneNachwelle == 0,
            $"T19 auf {geraet.ToUpperInvariant()}: {auswertbar.Count} auswertbare Zyklen, davon {ohneNachwelle} ohne Zeile 'alarm received from deviceId={a}' " +
            $"im audit.log nach dem Schließen. Die Nachwellen von A kamen nicht an, die Messung sagt nichts aus (Netzwerk und netzwerk.txt prüfen).");
    }

    private static async Task<MehrfachZeile> ZyklusAsync(HaelpMiApp app, Guid sender, string geraet, int n, bool klickmuster)
    {
        var zeile = new MehrfachZeile(n);
        var zaehler = new FensterZaehler(app);
        var popup = app.ErwartePopup($"T19 {geraet.ToUpperInvariant()} Zyklus {n}");
        zaehler.Original = Handle(popup);
        zaehler.Erfasse(klicken: false);

        var schliessen = Element(popup, "CloseButton").AsButton();
        Element(popup, "OnMyWayButton").AsButton().Invoke();
        var klick = DateTimeOffset.UtcNow;

        var frei = await SchnellWarten(() => { zaehler.Erfasse(klicken: false); return schliessen.IsEnabled; }, FreiTimeout);
        zeile.FreiNachMs = frei ? Ms(DateTimeOffset.UtcNow - klick) : null;
        var zu = false;
        if (frei)
        {
            schliessen.Invoke();
            zu = await SchnellWarten(() => !zaehler.Erfasse(klicken: false).Any(f => Handle(f) == zaehler.Original), SchliessenTimeout);
        }

        var zuSeit = DateTimeOffset.UtcNow;
        zeile.Haengt = !zu;
        if (zeile.Haengt)
        {
            // Ohne sauberes Schließen sagt die Nachwelle nichts aus; A überspringt sie bei "neustart".
            await AgentNeustartMitAnlaufAsync();
            zeile.Neustart = true;
        }

        Rendezvous.Signal($"t19-{n}-{geraet}-zu", zeile.Neustart ? "neustart" : string.Empty);

        var marke = Rendezvous.WaitForAsync($"t19-{n}-wellen", SchrittTimeout);
        while (!marke.IsCompleted)
        {
            zaehler.Erfasse(klickmuster);
            await Task.WhenAny(marke, Task.Delay(Takt));
        }

        var wellen = (await marke).Split('\n').Select(z => z.Trim()).ToArray();
        if (wellen[0] == "übersprungen")
        {
            zeile.Sitzung = wellen.ElementAtOrDefault(1) ?? string.Empty;
            zeile.Uebersprungen = true;
        }
        else
        {
            zeile.Sitzung = wellen[0];
            zeile.WellenGesendet = int.TryParse(wellen.ElementAtOrDefault(1), out var gesendet) ? gesendet : 0;
            zeile.WellenAcks = wellen.ElementAtOrDefault(2) ?? string.Empty;
            zeile.AbstaendeMs = wellen.ElementAtOrDefault(3) ?? string.Empty;
            var ende = DateTimeOffset.UtcNow + Nachlauf;
            while (DateTimeOffset.UtcNow < ende)
            {
                zaehler.Erfasse(klickmuster);
                await Task.Delay(Takt);
            }

            zeile.WellenEmpfangen = WellenSeit(sender, zuSeit).Count;
            zeile.Ignoriert = AuditLogZeilen(zuSeit).Count(z => z.Contains(IgnoriertMarke, StringComparison.Ordinal) && z.Contains(zeile.Sitzung, StringComparison.OrdinalIgnoreCase));
        }

        zeile.Uebernimm(zaehler);
        zeile.OffenAmEnde = zaehler.Erfasse(klicken: false).Count;
        if (zeile.OffenAmEnde > 0)
        {
            // Ein liegengebliebenes Fenster verfälschte den nächsten Zyklus.
            await AgentNeustartMitAnlaufAsync();
            zeile.Neustart = true;
        }

        return zeile;
    }

    private static string Zusammenfassung(string version, int zyklen, string geraet, bool klickmuster, List<MehrfachZeile> zeilen)
    {
        var auswertbar = zeilen.Where(z => !z.Uebersprungen).ToList();
        var treffer = auswertbar.Count(z => z.Treffer);
        return JsonSerializer.Serialize(new
        {
            agentVersion = version,
            produktRef = E2EUmgebung.ProduktRef,
            geraet = geraet.ToUpperInvariant(),
            zyklen,
            klickmuster,
            nachwellen = new[] { MinWellen, MaxWellen },
            abstandMs = new[] { MinAbstandMs, MaxAbstandMs },
            auswertbar = auswertbar.Count,
            treffer,
            quote = auswertbar.Count == 0 ? 0 : Math.Round((double)treffer / auswertbar.Count, 3),
            haengend = auswertbar.Count(z => z.Haengt),
            mehrAlsEinFenster = auswertbar.Count(z => z.Fenster > 1),
            ruecksetzungen = auswertbar.Count(z => z.Ruecksetzung),
            fensterMax = auswertbar.Count == 0 ? 0 : auswertbar.Max(z => z.Fenster),
            fensterSumme = auswertbar.Sum(z => z.Fenster),
            nachklicks = auswertbar.Sum(z => z.Nachklicks),
            ohneNachwelle = auswertbar.Count(z => z.WellenEmpfangen == 0),
            wenigerEmpfangenAlsGesendet = auswertbar.Count(z => z.WellenEmpfangen < z.WellenGesendet),
            ignoriertZeilen = auswertbar.Sum(z => z.Ignoriert),
            uebersprungen = zeilen.Count(z => z.Uebersprungen),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Zählt die verschiedenen Alarmfenster eines Zyklus über ihre Handles. Ein Fenster, das zwischen zwei Abfragen
    /// kurz fehlt (UIA-Aussetzer), zählt dadurch nicht doppelt.
    /// </summary>
    private sealed class FensterZaehler(HaelpMiApp app)
    {
        private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
        private readonly HashSet<IntPtr> _gesehen = new();
        private readonly HashSet<IntPtr> _onMyWayWarAus = new();
        private List<Window> _letzte = new();

        public IntPtr Original { get; set; }
        public int Anzahl => _gesehen.Count;
        public double? ZweitesNachMs { get; private set; }
        public bool Ruecksetzung { get; private set; }
        public int Nachklicks { get; private set; }

        /// <param name="klicken">Klickmuster: aktives "Bin unterwegs" anklicken, freies "Schließen" anklicken.</param>
        /// <returns>Die gerade offenen Alarmfenster.</returns>
        public List<Window> Erfasse(bool klicken)
        {
            List<Window> fenster;
            try
            {
                fenster = app.AlleAlarmfenster();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or FlaUIException)
            {
                // UIA-Aussetzer: lieber den letzten Stand als "kein Fenster", sonst gälte das Original als geschlossen.
                return _letzte;
            }

            foreach (var f in fenster)
            {
                try
                {
                    var h = Handle(f);
                    if (h == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (_gesehen.Add(h) && _gesehen.Count == 2)
                    {
                        ZweitesNachMs = Ms(DateTimeOffset.UtcNow - _start);
                    }

                    var onMyWay = Element(f, "OnMyWayButton").AsButton();
                    var aktiv = onMyWay.IsEnabled;
                    if (!aktiv)
                    {
                        _onMyWayWarAus.Add(h);
                    }

                    // Beim Original zählt nur der Wechsel aus -> aktiv, ein neues Fenster ist mit aktivem Knopf schon zurückgesetzt.
                    else if (h != Original || _onMyWayWarAus.Contains(h))
                    {
                        Ruecksetzung = true;
                    }

                    if (!klicken)
                    {
                        continue;
                    }

                    if (aktiv)
                    {
                        onMyWay.Invoke();
                        Nachklicks++;
                    }
                    else if (Element(f, "CloseButton").AsButton() is { IsEnabled: true } schliessen)
                    {
                        schliessen.Invoke();
                    }
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or FlaUIException)
                {
                    // Fenster wurde zwischen Aufzählen und Abfragen geschlossen.
                }
            }

            _letzte = fenster;
            return fenster;
        }
    }

    /// <summary>Eine CSV-Zeile: ein Zyklus auf einem Empfänger.</summary>
    private sealed class MehrfachZeile(int zyklus)
    {
        public const string Kopf =
            "zyklus;session_id;fenster;ruecksetzung;treffer;zweites_nach_ms;frei_nach_ms;haengt;wellen_gesendet;wellen_acks;abstaende_ms;" +
            "wellen_empfangen;ignoriert_zeilen;nachklicks;offen_am_ende;uebersprungen;neustart";

        public string Sitzung { get; set; } = string.Empty;
        public int Fenster { get; private set; }
        public bool Ruecksetzung { get; private set; }
        public bool Treffer => Fenster > 1 || Ruecksetzung;
        public double? ZweitesNachMs { get; private set; }
        public double? FreiNachMs { get; set; }
        public bool Haengt { get; set; }
        public int WellenGesendet { get; set; }
        public string WellenAcks { get; set; } = string.Empty;
        public string AbstaendeMs { get; set; } = string.Empty;
        public int WellenEmpfangen { get; set; }
        public int Ignoriert { get; set; }
        public int Nachklicks { get; private set; }
        public int OffenAmEnde { get; set; }
        public bool Uebersprungen { get; set; }
        public bool Neustart { get; set; }

        public void Uebernimm(FensterZaehler z)
        {
            Fenster = z.Anzahl;
            Ruecksetzung = z.Ruecksetzung;
            ZweitesNachMs = z.ZweitesNachMs;
            Nachklicks = z.Nachklicks;
        }

        public string Csv() => string.Join(';', zyklus, Sitzung, Fenster, R136Zeile.Bit(Ruecksetzung), R136Zeile.Bit(Treffer), R136Zeile.Zahl(ZweitesNachMs),
            R136Zeile.Zahl(FreiNachMs), R136Zeile.Bit(Haengt), WellenGesendet, WellenAcks, AbstaendeMs, WellenEmpfangen, Ignoriert, Nachklicks, OffenAmEnde,
            R136Zeile.Bit(Uebersprungen), R136Zeile.Bit(Neustart));

        public string Kurz() => Uebersprungen
            ? "übersprungen"
            : $"{Fenster} Fenster{(Ruecksetzung ? ", RÜCKSETZUNG" : string.Empty)}{(Haengt ? ", Schließen nie frei/Fenster blieb offen" : string.Empty)}, " +
              $"Nachwellen {WellenGesendet} gesendet ({WellenAcks}), {WellenEmpfangen} empfangen, ignoriert {Ignoriert}, Nachklicks {Nachklicks}";
    }
}
