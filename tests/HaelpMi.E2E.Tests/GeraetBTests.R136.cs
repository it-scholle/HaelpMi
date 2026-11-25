using System.Text;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.R136Messung;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T10, T18 und T19 (Alarmfenster öffnet nach dem Schließen erneut), Gegenstücke in GeraetATests.R136.cs und GeraetCTests.cs, Messung in R136Messung.cs.
public partial class GeraetBTests
{
    /// <summary>
    /// Empfänger-Seite und Auswertung. Pro Zyklus zwei Varianten:
    /// N (natürlich): "Bin unterwegs" mit Versatz φ zur nächsten erwarteten Repeat-Welle, φ wandert über die Zyklen
    /// von -400 bis +400 ms; sofort schließen, sobald frei; danach auf ein zweites Fenster derselben Session achten.
    /// W (Nachwelle): A schickt dieselbe Session nach dem Schließen erneut - deterministisch der Fehlerfall "Fenster öffnet nach dem Schließen erneut".
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
                var versatz = Versatz(n, zyklen);
                var seit = DateTimeOffset.UtcNow;
                await Rendezvous.WaitForAsync($"t10-{n}-aus", n == 1 ? GegenseiteTimeout : SchrittTimeout);
                var natuerlich = await NatuerlicheVarianteAsync(app, a, n, versatz, seit, perioden, "T10");
                var neustart = natuerlich.Neustart;
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

    // Gegenstücke: GeraetATests.T18_Regression136_DreiGeraete_SendetWellen, GeraetCTests.T18_Regression136_DreiGeraete_FensterBleibtZu.
    [Fact, TestPriority(57)]
    [Trait("Teil", DreiGeraete.Teil), Trait("Bereich", "R136")]
    public async Task T18_Regression136_DreiGeraete_FensterBleibtZu()
    {
        try
        {
            await Artefakte.SichereAsync(nameof(T18_Regression136_DreiGeraete_FensterBleibtZu),
                () => DreiGeraeteEmpfaengerAsync("b", _ausgabe), "t18");
        }
        catch
        {
            // Erst nach SichereAsync, damit der Screenshot eines Fehlschlags das Alarmfenster noch zeigt.
            await RaeumeAlarmfensterAufAsync("T18");
            throw;
        }
    }

    // Gegenstücke: GeraetATests.T19_Regression136_MehrfachNachwelle_SendetWellen, GeraetCTests.T19_Regression136_MehrfachNachwelle_EinFenster.
    // B klickt jedes wieder auftauchende Fenster sofort weg, wie beim Kunden beobachtet; C zählt nur.
    [Fact, TestPriority(58)]
    [Trait("Teil", DreiGeraete.Teil), Trait("Teil", R136Mehrfach.Teil), Trait("Bereich", "R136")]
    public async Task T19_Regression136_MehrfachNachwelle_EinFenster()
    {
        try
        {
            await Artefakte.SichereAsync(nameof(T19_Regression136_MehrfachNachwelle_EinFenster),
                () => R136Mehrfach.EmpfaengerAsync("b", klickmuster: true, _ausgabe), "t19");
        }
        catch
        {
            await RaeumeAlarmfensterAufAsync("T19");
            throw;
        }
    }
}
