using FlaUI.Core.AutomationElements;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T11/T12 mit drei Geräten, Gegenstücke in GeraetBTests.DreiGeraete.cs und GeraetCTests.cs.
public partial class GeraetATests
{
    [Fact, TestPriority(53), Trait("Bereich", "Basis")]
    [Trait("Teil", DreiGeraete.Teil)]
    public Task T11_Discovery_AKenntC() =>
        Artefakte.SichereAsync(nameof(T11_Discovery_AKenntC), async () =>
        {
            var c = await DeviceIdDerGegenseiteAsync("c");
            var eintrag = await WarteAufGeraetAsync(c, DiscoveryTimeout);
            _ausgabe.WriteLine($"C ({c}) in devices.json, Rolle {eintrag.Role}, Raum '{eintrag.RoomName}'.");
        });

    // Ein Alarm an zwei Empfänger mit Schwelle 2: B meldet zuerst, C danach; erst dann sind beide Fenster schließbar.
    [Fact, TestPriority(56), Trait("Bereich", "Basis")]
    [Trait("Teil", DreiGeraete.Teil), Trait("FR", "FR-47"), Trait("FR", "FR-50"), Trait("FR", "FR-53")]
    public async Task T12_Alarm_AnBUndC_Schwelle2()
    {
        try
        {
            await T12_Rumpf();
        }
        catch
        {
            // Auch beim Fehlschlag erst weiter, wenn C seinen Agent beendet hat, sonst läuft C in T6 hinein.
            try
            {
                await Rendezvous.WaitForAsync(DreiGeraete.CGestoppt, SchrittTimeout);
            }
            catch (TimeoutException ex)
            {
                _ausgabe.WriteLine($"T12: {ex.Message}");
            }

            throw;
        }
    }

    private Task T12_Rumpf() =>
        Artefakte.SichereAsync(nameof(T12_Alarm_AnBUndC_Schwelle2), async () =>
        {
            var (_, b, c) = await DreiGeraete.BereiteAlarmprofilVorAsync();

            var raumB = (await Rendezvous.WaitForAsync("t12-b-bereit", GegenseiteTimeout)).Trim();
            var raumC = (await Rendezvous.WaitForAsync("t12-c-bereit", GegenseiteTimeout)).Trim();
            await WarteAufGeraetAsync(b, DiscoveryTimeout);
            await WarteAufGeraetAsync(c, DiscoveryTimeout);

            var befunde = new List<string>();
            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            DrueckeHotkey(AlarmHotkey);
            Rendezvous.Signal("t12-ausgeloest", $"{E2EUmgebung.Raum} ({E2EUmgebung.Raumnummer})\n{DateTimeOffset.UtcNow:O}");
            var banner = app.ErwarteBanner("T12: Alarm A an B und C");

            bool Unterwegs(string raum) => AufDemWeg(banner).Any(n => n.StartsWith(raum + " - ", StringComparison.Ordinal));
            try
            {
                await Rendezvous.WaitForAsync("t12-c-unterwegs", SchrittTimeout);
                await WarteBisAsync(() => Unterwegs(raumB) && Unterwegs(raumC), TimeSpan.FromSeconds(15),
                    () => $"'Auf dem Weg' zeigt nicht B ('{raumB}') und C ('{raumC}'): {string.Join(" | ", AufDemWeg(banner))}");
                await PruefeBefundAsync(befunde, () => WarteBisAsync(() => NameVon(banner, "AckedText") == "Empfangen: 2 von 2", TimeSpan.FromSeconds(15),
                    () => $"Produktbefund: das Sender-Banner zeigt '{NameVon(banner, "AckedText")}' statt 'Empfangen: 2 von 2', " +
                        "obwohl B und C den Alarm empfangen und 'Bin unterwegs' gemeldet haben."));
            }
            catch
            {
                // Sonst wiederholt A den Alarm bis in T6 hinein.
                try
                {
                    Element(banner, "CancelButton").AsButton().Invoke();
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
                {
                }

                await app.SchliesseSenderBannerAsync();
                throw;
            }

            await app.SchliesseBannerAsync(banner);
            Rendezvous.Signal("t12-a-fertig");
            // C beendet danach seinen Agent; ab T6 soll C nicht mehr mitspielen (siehe GeraetCTests).
            await Rendezvous.WaitForAsync(DreiGeraete.CGestoppt, SchrittTimeout);
            MeldeBefunde(befunde);
        }, "t12");
}
