using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using Xunit;
using Xunit.Abstractions;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

/// <summary>
/// Messung für die Regressionstests "Alarmfenster öffnet nach dem Schließen erneut" auf Empfängerseite: T10 (B) und T18 (B und C). Ein Zyklus in Variante N
/// klickt "Bin unterwegs" mit einem Versatz zur nächsten erwarteten Repeat-Welle, schließt sofort und achtet danach
/// auf ein zweites Alarmfenster derselben Session. Ergebnisse als CSV-Zeilen (<see cref="R136Zeile"/>).
/// </summary>
internal static class R136Messung
{
    public static readonly TimeSpan BeobachtungsDauer = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan StandardPeriode = TimeSpan.FromSeconds(5);
    public const int MaxVersatzMs = 400;
    public const string IgnoriertMarke = "abgeschlossene Session ignoriert";

    /// <summary>Versatz φ für Zyklus <paramref name="n"/>: wandert gleichmäßig von -<see cref="MaxVersatzMs"/> bis +<see cref="MaxVersatzMs"/>.</summary>
    public static int Versatz(int n, int zyklen) =>
        zyklen == 1 ? 0 : -MaxVersatzMs + 2 * MaxVersatzMs * (n - 1) / (zyklen - 1);

    /// <summary>
    /// Variante N eines Zyklus, ab dem Eintreffen der Auslöse-Marke: Alarmfenster erwarten, "Bin unterwegs" mit
    /// <paramref name="versatzMs"/> zur nächsten Welle, sofort schließen, sobald frei, <see cref="BeobachtungsDauer"/>
    /// auf ein zweites Fenster achten. Zeigt sich ein zweites Fenster, wird Schließen nie frei oder bleibt das Fenster
    /// offen, startet der Agent neu (<see cref="R136Zeile.Neustart"/>), damit der nächste Zyklus sauber beginnt.
    /// </summary>
    /// <param name="seit">Zeitpunkt vor dem Warten auf die Auslöse-Marke; ab da zählen die Wellen im audit.log.</param>
    /// <param name="perioden">Gemessene Wellenabstände aller bisherigen Zyklen in ms; wird ergänzt.</param>
    public static async Task<R136Zeile> NatuerlicheVarianteAsync(
        HaelpMiApp app, Guid sender, int n, int versatzMs, DateTimeOffset seit, List<double> perioden, string fall)
    {
        var markeDa = DateTimeOffset.UtcNow;
        var popup = app.ErwartePopup($"{fall} Zyklus {n}");
        var natuerlich = new R136Zeile(n, "N", versatzMs) { PopupLatenzMs = Ms(DateTimeOffset.UtcNow - markeDa) };
        var original = Handle(popup);
        var onMyWay = Element(popup, "OnMyWayButton").AsButton();
        var schliessen = Element(popup, "CloseButton").AsButton();

        // Wellenphase aus den eigenen Empfangszeiten (Uhr dieses Geräts); ohne Messwert erst die zweite Welle abwarten.
        var wellen = await WarteAufWellenAsync(sender, seit, perioden.Count == 0 ? 2 : 1);
        var periode = perioden.Count > 0 ? TimeSpan.FromMilliseconds(perioden.Average()) : wellen[^1] - wellen[^2];
        var ziel = wellen[^1] + periode + TimeSpan.FromMilliseconds(versatzMs);
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
        perioden.AddRange(Abstaende(WellenSeit(sender, seit)));

        var neustart = natuerlich.Treffer || natuerlich.Haengt || beobachtung.OriginalNochOffen;
        if (neustart)
        {
            await AgentNeustartMitAnlaufAsync();
        }

        natuerlich.Neustart = neustart;
        return natuerlich;
    }

    /// <summary>
    /// T18 auf B und C (gleicher Ablauf): pro Zyklus Variante N gegen einen Alarm A an B und C mit Schwelle 2. Beide
    /// Empfänger zielen mit demselben Versatz auf dieselbe Welle; erst die zweite Rückmeldung gibt beide Fenster frei.
    /// Ohne produkt_ref rot bei einem zweiten Fenster, bei nie freiem Schließen oder bei einem Fenster, das nach dem
    /// Schließen offen blieb; mit produkt_ref (Vergleichslauf gegen einen alten Stand) wird nur gemessen.
    /// </summary>
    /// <param name="geraet">"b" oder "c"; Teil der Marken und Dateinamen.</param>
    public static async Task DreiGeraeteEmpfaengerAsync(string geraet, ITestOutputHelper ausgabe)
    {
        var (a, _, _) = await DreiGeraete.BereiteAlarmprofilVorAsync(DreiGeraete.ConfigVersionR136);
        await WarteAufGeraetAsync(a, DiscoveryTimeout);
        Rendezvous.Signal($"t18-{geraet}-bereit");

        using var app = new HaelpMiApp();
        var zyklen = E2EUmgebung.Zyklen;
        var version = AgentVersion();
        var csv = Path.Combine(Artefakte.Ordner, $"r136-drei-{geraet}-{version}.csv");
        Directory.CreateDirectory(Artefakte.Ordner);
        File.WriteAllText(csv, R136Zeile.Kopf + Environment.NewLine, Encoding.UTF8);

        var zeilen = new List<R136Zeile>();
        var perioden = new List<double>();
        for (var n = 1; n <= zyklen; n++)
        {
            var seit = DateTimeOffset.UtcNow;
            await Rendezvous.WaitForAsync($"t18-{n}-aus", n == 1 ? GegenseiteTimeout : SchrittTimeout);
            var zeile = await NatuerlicheVarianteAsync(app, a, n, Versatz(n, zyklen), seit, perioden, $"T18 {geraet.ToUpperInvariant()}");
            Rendezvous.Signal($"t18-{n}-{geraet}-zu", zeile.Neustart ? "neustart" : string.Empty);

            // A meldet die Session-ID erst, wenn beide Empfänger fertig sind; erst dann beginnt der nächste Zyklus.
            zeile.Sitzung = (await Rendezvous.WaitForAsync($"t18-{n}-fertig", SchrittTimeout)).Trim();
            zeilen.Add(zeile);
            File.AppendAllText(csv, zeile.Csv() + Environment.NewLine, Encoding.UTF8);
            ausgabe.WriteLine($"Zyklus {n}: {zeile.Kurz()}");
        }

        var zusammenfassung = Zusammenfassung(version, zyklen, zeilen);
        File.WriteAllText(Path.Combine(Artefakte.Ordner, $"r136-drei-{geraet}-zusammenfassung.json"), zusammenfassung, Encoding.UTF8);
        ausgabe.WriteLine(zusammenfassung);

        var fehler = zeilen.Count(z => z.Treffer || z.Haengt || z.OriginalNochOffen);
        if (E2EUmgebung.ProduktRef is { } produkt)
        {
            ausgabe.WriteLine($"Vergleichslauf gegen {produkt} (Agent {version}): nur gemessen, {fehler} von {zeilen.Count} Zyklen mit zweitem, nicht schließbarem oder offen gebliebenem Fenster.");
            return;
        }

        Assert.True(fehler == 0,
            $"Alarmfenster nach dem Schließen (drei Geräte) auf {geraet.ToUpperInvariant()}: in {fehler} von {zeilen.Count} Zyklen erschien ein zweites Alarmfenster, " +
            $"wurde Schließen nicht frei oder blieb das Fenster offen (Agent {version}). Details: {Path.GetFileName(csv)} im Artefakt.");
    }

    public static string AgentVersion()
    {
        var info = FileVersionInfo.GetVersionInfo(Path.Combine(E2EUmgebung.ProgrammOrdner, AgentProzess + ".exe"));
        return Regex.Replace(info.FileVersion ?? "unbekannt", "[^0-9A-Za-z.]", "_");
    }

    /// <summary>Empfangszeitpunkte der Wellen von <paramref name="sender"/> laut audit.log ("alarm received", gibt es auch in v0.46.1).</summary>
    public static List<DateTimeOffset> WellenSeit(Guid sender, DateTimeOffset seit) =>
        AuditLogZeilen(seit)
            .Where(z => z.Contains($"alarm received from deviceId={sender}", StringComparison.OrdinalIgnoreCase))
            .Select(z => DateTimeOffset.Parse(z[..z.IndexOf('\t')], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .OrderBy(t => t)
            .ToList();

    /// <returns>Mindestens <paramref name="anzahl"/> Empfangszeitpunkte, bei 1 ergänzt um einen geschätzten Vorgänger.</returns>
    public static async Task<List<DateTimeOffset>> WarteAufWellenAsync(Guid sender, DateTimeOffset seit, int anzahl)
    {
        var frist = DateTime.UtcNow + StandardPeriode * 3;
        List<DateTimeOffset> wellen;
        while ((wellen = WellenSeit(sender, seit)).Count < anzahl)
        {
            if (DateTime.UtcNow > frist)
            {
                Assert.Fail($"audit.log auf {E2EUmgebung.Raum} enthält nach {(StandardPeriode * 3).TotalSeconds:0} s nur {wellen.Count} statt {anzahl} Zeile(n) 'alarm received from deviceId={sender}'.");
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
    public static IEnumerable<double> Abstaende(List<DateTimeOffset> wellen) =>
        wellen.Zip(wellen.Skip(1), (x, y) => (y - x).TotalMilliseconds).Where(ms => ms is > 3000 and < 10000);

    public static async Task WarteBisZeitpunktAsync(DateTimeOffset ziel)
    {
        var rest = ziel - DateTimeOffset.UtcNow;
        if (rest > TimeSpan.FromMilliseconds(50))
        {
            await Task.Delay(rest - TimeSpan.FromMilliseconds(30));
        }

        SpinWait.SpinUntil(() => DateTimeOffset.UtcNow >= ziel);
    }

    /// <summary>Wie WarteBisAsync, aber im 20-ms-Takt und ohne Assert.</summary>
    public static async Task<bool> SchnellWarten(Func<bool> bedingung, TimeSpan timeout)
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
            catch (Exception ex) when (ex is COMException or InvalidOperationException or FlaUIException)
            {
            }

            await Task.Delay(20);
        }

        return false;
    }

    /// <summary>Achtet <paramref name="dauer"/> lang auf ein Alarmfenster, dessen Handle nicht <paramref name="original"/> ist.</summary>
    public static async Task<R136Beobachtung> BeobachteAsync(HaelpMiApp app, IntPtr original, TimeSpan dauer)
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

    public static string Zusammenfassung(string version, int zyklen, List<R136Zeile> zeilen)
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

    public static double Ms(TimeSpan t) => Math.Round(t.TotalMilliseconds);

    public sealed record R136Beobachtung(bool Treffer, double? ErschienenNachMs, bool? OnMyWayAktiv, bool? SchliessenGesperrt, double? OffenMs, bool OriginalNochOffen);

    /// <summary>Eine CSV-Zeile: ein Zyklus in einer Variante.</summary>
    public sealed class R136Zeile(int zyklus, string variante, int versatzMs)
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

        public static string Zahl(double? wert) => wert?.ToString("0", CultureInfo.InvariantCulture) ?? string.Empty;

        public static string Bit(bool? wert) => wert is null ? string.Empty : wert.Value ? "1" : "0";
    }
}
