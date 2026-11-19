using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T10 Regressionstest #136, Gegenstück in GeraetATests.R136.cs.
public partial class GeraetBTests
{
    private static readonly TimeSpan BeobachtungsDauer = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan StandardPeriode = TimeSpan.FromSeconds(5);
    private const int MaxVersatzMs = 400;
    private const string IgnoriertMarke = "abgeschlossene Session ignoriert";

    /// <summary>
    /// Empfänger-Seite und Auswertung. Pro Zyklus zwei Varianten:
    /// N (natürlich): "Bin unterwegs" mit Versatz φ zur nächsten erwarteten Repeat-Welle, φ wandert über die Zyklen
    /// von -400 bis +400 ms; sofort schließen, sobald frei; danach auf ein zweites Fenster derselben Session achten.
    /// W (Nachwelle): A schickt dieselbe Session nach dem Schließen erneut - deterministisch der Fall aus #136.
    /// Ohne produkt_ref muss W ohne Treffer bleiben, mit produkt_ref (Vergleichslauf gegen einen alten Stand) wird nur gemessen.
    /// </summary>
    [Fact, TestPriority(100)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R136")]
    public Task T10_Regression136_FensterBleibtZu() =>
        Artefakte.SichereAsync(nameof(T10_Regression136_FensterBleibtZu), async () =>
        {
            var b = EigeneDeviceId();
            var a = await DeviceIdDerGegenseiteAsync("a");
            await BereiteAlarmprofilVorAsync(a, b, 5000);
            await WarteAufGeraetAsync(a, DiscoveryTimeout);
            Rendezvous.Signal("t10-b-bereit");

            using var app = new HaelpMiApp();
            var zyklen = E2EUmgebung.Zyklen;
            var version = AgentVersion();
            var csv = Path.Combine(Artefakte.Ordner, $"r136-{version}.csv");
            Directory.CreateDirectory(Artefakte.Ordner);
            File.WriteAllText(csv, R136Zeile.Kopf + Environment.NewLine, Encoding.UTF8);

            var zeilen = new List<R136Zeile>();
            var perioden = new List<double>();
            for (var n = 1; n <= zyklen; n++)
            {
                var versatz = zyklen == 1 ? 0 : -MaxVersatzMs + 2 * MaxVersatzMs * (n - 1) / (zyklen - 1);
                var seit = DateTimeOffset.UtcNow;
                await Rendezvous.WaitForAsync($"t10-{n}-aus", n == 1 ? GegenseiteTimeout : SchrittTimeout);
                var markeDa = DateTimeOffset.UtcNow;
                var popup = app.ErwartePopup($"T10 Zyklus {n}");
                var natuerlich = new R136Zeile(n, "N", versatz) { PopupLatenzMs = Ms(DateTimeOffset.UtcNow - markeDa) };
                var original = Handle(popup);
                var onMyWay = Element(popup, "OnMyWayButton").AsButton();
                var schliessen = Element(popup, "CloseButton").AsButton();

                // Wellenphase aus den eigenen Empfangszeiten (Uhr von B); ohne Messwert erst die zweite Welle abwarten.
                var wellen = await WarteAufWellenAsync(a, seit, perioden.Count == 0 ? 2 : 1);
                var periode = perioden.Count > 0 ? TimeSpan.FromMilliseconds(perioden.Average()) : wellen[^1] - wellen[^2];
                var ziel = wellen[^1] + periode + TimeSpan.FromMilliseconds(versatz);
                while (ziel - DateTimeOffset.UtcNow < TimeSpan.FromMilliseconds(150))
                {
                    ziel += periode;
                }

                natuerlich.PeriodeMs = Ms(periode);
                await WarteBisZeitpunktAsync(ziel);
                var klick = DateTimeOffset.UtcNow;
                onMyWay.Invoke();
                natuerlich.KlickAbweichungMs = Ms(klick - ziel);

                var frei = await SchnellWarten(() => schliessen.IsEnabled, TimeSpan.FromSeconds(15));
                natuerlich.FreiNachMs = frei ? Ms(DateTimeOffset.UtcNow - klick) : null;
                if (frei)
                {
                    schliessen.Invoke();
                }

                var beobachtung = await BeobachteAsync(app, original, BeobachtungsDauer);
                natuerlich.Uebernimm(beobachtung);
                natuerlich.Haengt = !frei;
                natuerlich.Ignoriert = AuditLogZeilen(klick).Count(z => z.Contains(IgnoriertMarke, StringComparison.Ordinal));
                perioden.AddRange(Abstaende(WellenSeit(a, seit)));

                var neustart = natuerlich.Treffer || natuerlich.Haengt || beobachtung.OriginalNochOffen;
                if (neustart)
                {
                    await AgentNeustartMitAnlaufAsync();
                }

                natuerlich.Neustart = neustart;
                var zuSeit = DateTimeOffset.UtcNow;
                Rendezvous.Signal($"t10-{n}-zu", neustart ? "neustart" : string.Empty);

                var nach = (await Rendezvous.WaitForAsync($"t10-{n}-nachwelle", SchrittTimeout)).Split('\n').Select(z => z.Trim()).ToArray();
                var nachwelle = new R136Zeile(n, "W", versatz);
                if (nach[0] == "übersprungen")
                {
                    natuerlich.Sitzung = nachwelle.Sitzung = nach.ElementAtOrDefault(1) ?? string.Empty;
                    nachwelle.Uebersprungen = true;
                }
                else
                {
                    natuerlich.Sitzung = nachwelle.Sitzung = nach[0];
                    nachwelle.NachwelleAck = nach.ElementAtOrDefault(1) ?? string.Empty;
                    var w = await BeobachteAsync(app, IntPtr.Zero, BeobachtungsDauer);
                    nachwelle.Uebernimm(w);
                    nachwelle.Ignoriert = AuditLogZeilen(zuSeit).Count(z => z.Contains(IgnoriertMarke, StringComparison.Ordinal) && z.Contains(nach[0], StringComparison.OrdinalIgnoreCase));
                    if (w.Treffer)
                    {
                        await AgentNeustartMitAnlaufAsync();
                        nachwelle.Neustart = true;
                    }
                }

                zeilen.Add(natuerlich);
                zeilen.Add(nachwelle);
                File.AppendAllText(csv, natuerlich.Csv() + Environment.NewLine + nachwelle.Csv() + Environment.NewLine, Encoding.UTF8);
                _ausgabe.WriteLine($"Zyklus {n}: {natuerlich.Kurz()} | {nachwelle.Kurz()}");
                Rendezvous.Signal($"t10-{n}-fertig");
            }

            var zusammenfassung = Zusammenfassung(version, zyklen, zeilen);
            File.WriteAllText(Path.Combine(Artefakte.Ordner, "r136-zusammenfassung.json"), zusammenfassung, Encoding.UTF8);
            _ausgabe.WriteLine(zusammenfassung);

            var wTreffer = zeilen.Count(z => z.Variante == "W" && z.Treffer);
            var wAuswertbar = zeilen.Count(z => z.Variante == "W" && !z.Uebersprungen);
            if (E2EUmgebung.ProduktRef is { } produkt)
            {
                _ausgabe.WriteLine($"Vergleichslauf gegen {produkt} (Agent {version}): nur gemessen, Nachwelle {wTreffer} von {wAuswertbar} Zyklen mit zweitem Fenster.");
                return;
            }

            Assert.True(wTreffer == 0,
                $"Regression #136: nach einer Nachwelle derselben Session erschien in {wTreffer} von {wAuswertbar} Zyklen erneut ein Alarmfenster " +
                $"(Agent {version}). Details: {Path.GetFileName(csv)} und r136-zusammenfassung.json im Artefakt.");
        }, "t10");

    private static string AgentVersion()
    {
        var info = FileVersionInfo.GetVersionInfo(Path.Combine(E2EUmgebung.ProgrammOrdner, AgentProzess + ".exe"));
        return Regex.Replace(info.FileVersion ?? "unbekannt", "[^0-9A-Za-z.]", "_");
    }

    /// <summary>Empfangszeitpunkte der Wellen von <paramref name="sender"/> laut audit.log ("alarm received", gibt es auch in v0.46.1).</summary>
    private static List<DateTimeOffset> WellenSeit(Guid sender, DateTimeOffset seit) =>
        AuditLogZeilen(seit)
            .Where(z => z.Contains($"alarm received from deviceId={sender}", StringComparison.OrdinalIgnoreCase))
            .Select(z => DateTimeOffset.Parse(z[..z.IndexOf('\t')], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .OrderBy(t => t)
            .ToList();

    /// <returns>Mindestens <paramref name="anzahl"/> Empfangszeitpunkte, bei 1 ergänzt um einen geschätzten Vorgänger.</returns>
    private static async Task<List<DateTimeOffset>> WarteAufWellenAsync(Guid sender, DateTimeOffset seit, int anzahl)
    {
        var frist = DateTime.UtcNow + StandardPeriode * 3;
        List<DateTimeOffset> wellen;
        while ((wellen = WellenSeit(sender, seit)).Count < anzahl)
        {
            if (DateTime.UtcNow > frist)
            {
                Assert.Fail($"audit.log auf B enthält nach {(StandardPeriode * 3).TotalSeconds:0} s nur {wellen.Count} statt {anzahl} Zeile(n) 'alarm received from deviceId={sender}'.");
            }

            await Task.Delay(100);
        }

        if (wellen.Count == 1)
        {
            wellen.Insert(0, wellen[0] - StandardPeriode);
        }

        return wellen;
    }

    // Plausible Abstände aufeinanderfolgender Wellen; größere Lücken entstehen durch das Schließen, kleinere gibt es nicht.
    private static IEnumerable<double> Abstaende(List<DateTimeOffset> wellen) =>
        wellen.Zip(wellen.Skip(1), (x, y) => (y - x).TotalMilliseconds).Where(ms => ms is > 3000 and < 10000);

    private static async Task WarteBisZeitpunktAsync(DateTimeOffset ziel)
    {
        var rest = ziel - DateTimeOffset.UtcNow;
        if (rest > TimeSpan.FromMilliseconds(50))
        {
            await Task.Delay(rest - TimeSpan.FromMilliseconds(30));
        }

        SpinWait.SpinUntil(() => DateTimeOffset.UtcNow >= ziel);
    }

    /// <summary>Wie WarteBisAsync, aber im 20-ms-Takt und ohne Assert.</summary>
    private static async Task<bool> SchnellWarten(Func<bool> bedingung, TimeSpan timeout)
    {
        var frist = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < frist)
        {
            try
            {
                if (bedingung())
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
            }

            await Task.Delay(20);
        }

        return false;
    }

    /// <summary>Achtet <paramref name="dauer"/> lang auf ein Alarmfenster, dessen Handle nicht <paramref name="original"/> ist.</summary>
    private static async Task<R136Beobachtung> BeobachteAsync(HaelpMiApp app, IntPtr original, TimeSpan dauer)
    {
        var start = DateTimeOffset.UtcNow;
        IntPtr zweites = IntPtr.Zero;
        double? erschienenMs = null, offenMs = null;
        bool? onMyWayAktiv = null, schliessenGesperrt = null;
        var originalOffen = false;
        while (DateTimeOffset.UtcNow - start < dauer)
        {
            var fenster = app.AlleAlarmfenster();
            originalOffen = original != IntPtr.Zero && fenster.Any(f => Handle(f) == original);
            var anderes = fenster.FirstOrDefault(f => Handle(f) != original && Handle(f) != IntPtr.Zero);
            if (zweites == IntPtr.Zero && anderes is not null)
            {
                zweites = Handle(anderes);
                erschienenMs = Ms(DateTimeOffset.UtcNow - start);
                try
                {
                    onMyWayAktiv = Element(anderes, "OnMyWayButton").IsEnabled;
                    schliessenGesperrt = !Element(anderes, "CloseButton").IsEnabled;
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException)
                {
                }
            }
            else if (zweites != IntPtr.Zero && offenMs is null && fenster.All(f => Handle(f) != zweites))
            {
                offenMs = Ms(DateTimeOffset.UtcNow - start) - erschienenMs;
            }

            await Task.Delay(100);
        }

        return new R136Beobachtung(zweites != IntPtr.Zero, erschienenMs, onMyWayAktiv, schliessenGesperrt, offenMs, originalOffen);
    }

    private static string Zusammenfassung(string version, int zyklen, List<R136Zeile> zeilen)
    {
        object Variante(string v)
        {
            var auswertbar = zeilen.Where(z => z.Variante == v && !z.Uebersprungen).ToList();
            var treffer = auswertbar.Count(z => z.Treffer);
            return new
            {
                auswertbar = auswertbar.Count,
                treffer,
                quote = auswertbar.Count == 0 ? 0 : Math.Round((double)treffer / auswertbar.Count, 3),
                haengend = auswertbar.Count(z => z.Haengt || z.SchliessenGesperrt == true),
                ignoriertZeilen = auswertbar.Sum(z => z.Ignoriert),
            };
        }

        return JsonSerializer.Serialize(new
        {
            agentVersion = version,
            produktRef = E2EUmgebung.ProduktRef,
            zyklen,
            versatzMs = new[] { -MaxVersatzMs, MaxVersatzMs },
            N = Variante("N"),
            W = Variante("W"),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static double Ms(TimeSpan t) => Math.Round(t.TotalMilliseconds);

    private sealed record R136Beobachtung(bool Treffer, double? ErschienenNachMs, bool? OnMyWayAktiv, bool? SchliessenGesperrt, double? OffenMs, bool OriginalNochOffen);

    /// <summary>Eine CSV-Zeile: ein Zyklus in einer Variante.</summary>
    private sealed class R136Zeile(int zyklus, string variante, int versatzMs)
    {
        public const string Kopf =
            "zyklus;variante;versatz_ms;session_id;popup_latenz_ms;periode_ms;klick_abweichung_ms;frei_nach_ms;haengt;" +
            "zweites_fenster;zweites_nach_ms;onmyway_aktiv;schliessen_gesperrt;zweites_offen_ms;original_noch_offen;" +
            "ignoriert_zeilen;nachwelle_ack;uebersprungen;neustart";

        public string Variante { get; } = variante;
        public string Sitzung { get; set; } = string.Empty;
        public double? PopupLatenzMs { get; set; }
        public double? PeriodeMs { get; set; }
        public double? KlickAbweichungMs { get; set; }
        public double? FreiNachMs { get; set; }
        public bool Haengt { get; set; }
        public bool Treffer { get; private set; }
        public double? ZweitesNachMs { get; private set; }
        public bool? OnMyWayAktiv { get; private set; }
        public bool? SchliessenGesperrt { get; private set; }
        public double? ZweitesOffenMs { get; private set; }
        public bool OriginalNochOffen { get; private set; }
        public int Ignoriert { get; set; }
        public string NachwelleAck { get; set; } = string.Empty;
        public bool Uebersprungen { get; set; }
        public bool Neustart { get; set; }

        public void Uebernimm(R136Beobachtung b)
        {
            Treffer = b.Treffer;
            ZweitesNachMs = b.ErschienenNachMs;
            OnMyWayAktiv = b.OnMyWayAktiv;
            SchliessenGesperrt = b.SchliessenGesperrt;
            ZweitesOffenMs = b.OffenMs;
            OriginalNochOffen = b.OriginalNochOffen;
        }

        // Leeres zweites_offen_ms bei zweites_fenster=1: Fenster war am Ende der Beobachtung noch offen.
        public string Csv() => string.Join(';', zyklus, Variante, versatzMs, Sitzung, Zahl(PopupLatenzMs), Zahl(PeriodeMs), Zahl(KlickAbweichungMs),
            Zahl(FreiNachMs), Bit(Haengt), Bit(Treffer), Zahl(ZweitesNachMs), Bit(OnMyWayAktiv), Bit(SchliessenGesperrt), Zahl(ZweitesOffenMs),
            Bit(OriginalNochOffen), Ignoriert, NachwelleAck, Bit(Uebersprungen), Bit(Neustart));

        public string Kurz() => Uebersprungen
            ? $"{Variante}: übersprungen"
            : $"{Variante}: {(Treffer ? $"ZWEITES FENSTER (Bin unterwegs {(OnMyWayAktiv == true ? "aktiv" : "inaktiv")}, Schließen {(SchliessenGesperrt == true ? "gesperrt" : "frei")})" : "kein zweites Fenster")}" +
              $"{(Haengt ? ", Schließen nie frei" : string.Empty)}, ignoriert {Ignoriert}";

        private static string Zahl(double? wert) => wert?.ToString("0", CultureInfo.InvariantCulture) ?? string.Empty;

        private static string Bit(bool? wert) => wert is null ? string.Empty : wert.Value ? "1" : "0";
    }
}
