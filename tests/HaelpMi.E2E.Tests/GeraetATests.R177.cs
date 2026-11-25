using FlaUI.Core.AutomationElements;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T20 Regressionstest #177, Gegenstück in GeraetBTests.R177.cs.
public partial class GeraetATests
{
    /// <summary>
    /// Alarm A an B ohne "Bin unterwegs". Nach dem Beenden des Agents auf B läuft jede Welle in den Quittungs-Timeout;
    /// A muss trotzdem im 5-s-Takt weitersenden, statt pro Welle auf Quittung und Status-Relay zu warten.
    /// </summary>
    [Fact, TestPriority(115)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R177")]
    public Task T20_Regression177_TaktOhneQuittung() =>
        Artefakte.SichereAsync(nameof(T20_Regression177_TaktOhneQuittung), async () =>
        {
            var a = EigeneDeviceId();
            var b = await DeviceIdDerGegenseiteAsync("b");
            await BereiteAlarmprofilVorAsync(a, b, R177Takt.ConfigVersion);
            await Rendezvous.WaitForAsync("t20-b-bereit", GegenseiteTimeout);
            await WarteAufGeraetAsync(b, DiscoveryTimeout);

            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            DrueckeHotkey(AlarmHotkey);
            Rendezvous.Signal("t20-aus");
            var banner = app.ErwarteBanner("T20");
            List<TimeSpan> abstaende;
            try
            {
                await Rendezvous.WaitForAsync("t20-b-weg", SchrittTimeout);
                var seit = DateTimeOffset.UtcNow;
                await Task.Delay(R177Takt.Beobachtung);
                var wellen = R177Takt.Zeitpunkte(seit, $"alarm sent alarmProfileId={ProfilId}");
                abstaende = R177Takt.Abstaende(wellen);
                R177Takt.Sichere("t20-takt-a.txt",
                    $"{wellen.Count} Wellen in {R177Takt.Beobachtung.TotalSeconds:0} s ohne Quittung, Abstände: {R177Takt.Text(abstaende)}");
                Assert.True(wellen.Count >= 3,
                    $"Regression #177: A sendete in {R177Takt.Beobachtung.TotalSeconds:0} s ohne Quittung nur {wellen.Count} Welle(n) (Abstände {R177Takt.Text(abstaende)}), erwartet mindestens 3 im 5-s-Takt.");
            }
            finally
            {
                try
                {
                    Element(banner, "CancelButton").AsButton().Invoke();
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
                {
                }

                await app.SchliesseSenderBannerAsync();
                Rendezvous.Signal("t20-fertig");
            }

            Assert.True(abstaende.All(x => x <= R177Takt.MaxAbstand),
                $"Regression #177: Wellenabstände ohne Quittung {R177Takt.Text(abstaende)}, erwartet höchstens {R177Takt.MaxAbstand.TotalSeconds:0} s.");
        }, "t20");
}
