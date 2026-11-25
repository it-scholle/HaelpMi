using FlaUI.Core.AutomationElements;
using HaelpMi.Core.Storage;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T23, Gegenstück in GeraetBTests.R73.cs.
public partial class GeraetATests
{
    // Aus dem Dokumentationsbereich TEST-NET-1 (RFC 5737): im Testnetz nie vergeben, Verbindungen laufen ins Leere.
    private const string R73VeralteteAdresse = "192.0.2.73";

    private static readonly TimeSpan R73EmpfangsFrist = TimeSpan.FromSeconds(90);

    /// <summary>
    /// A kennt B nur unter einer veralteten Adresse und löst aus, während der Agent auf B noch nicht läuft.
    /// Danach startet B und meldet per Boot-Call die aktuelle Adresse. Spätestens die nächste Welle muss B
    /// erreichen; vorher lief die ganze Session an die beim Auslösen gespeicherte Adresse.
    /// </summary>
    [Fact, TestPriority(170)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R73")]
    public Task T23_Regression73_EmpfaengerStartetWaehrendAlarm() =>
        Artefakte.SichereAsync(nameof(T23_Regression73_EmpfaengerStartetWaehrendAlarm), async () =>
        {
            var a = EigeneDeviceId();
            var b = await DeviceIdDerGegenseiteAsync("b");
            await BereiteAlarmprofilVorAsync(a, b, 7000);
            await Rendezvous.WaitForAsync("t23-b-bereit", GegenseiteTimeout);
            await WarteAufGeraetAsync(b, DiscoveryTimeout);
            await Rendezvous.WaitForAsync("t23-b-weg", SchrittTimeout);

            BeendeAlles();
            var geraete = new DeviceStore().Load();
            var eintragB = Assert.Single(geraete, d => d.DeviceId == b);
            eintragB.IpAddress = R73VeralteteAdresse;
            new DeviceStore().Save(geraete);
            await StarteAgentAsync();
            await Task.Delay(AgentAnlaufzeit);

            var vorDemAusloesen = LiesGeraete().Single(d => d.DeviceId == b);
            Assert.True(vorDemAusloesen.IpAddress == R73VeralteteAdresse,
                $"T23: Vorbereitung gescheitert, A kennt B vor dem Auslösen schon unter '{vorDemAusloesen.IpAddress}' " +
                "(läuft der Agent auf B oder C noch?).");

            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            DrueckeHotkey(AlarmHotkey);
            var banner = app.ErwarteBanner("T23");
            var ausgeloest = DateTimeOffset.UtcNow;
            Rendezvous.Signal("t23-aus");
            try
            {
                await WarteBisAsync(() => NameVon(banner, "AckedText") == "Empfangen: 1 von 1", R73EmpfangsFrist,
                    () =>
                    {
                        var adresse = LiesGeraete().SingleOrDefault(d => d.DeviceId == b)?.IpAddress;
                        var wellen = AuditLogZeilen(ausgeloest).Count(z => z.Contains($"alarm sent alarmProfileId={ProfilId}", StringComparison.Ordinal));
                        return $"T23: B hat {R73EmpfangsFrist.TotalSeconds:0} s nach dem Auslösen nicht quittiert, Banner zeigt " +
                            $"'{NameVon(banner, "AckedText")}'. A kennt B inzwischen unter '{adresse}', {wellen} Welle(n) gesendet.";
                    });
            }
            finally
            {
                try
                {
                    Element(banner, "CancelButton").AsButton().Invoke();
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
                {
                    Console.WriteLine($"T23: Abbrechen im Sender-Banner nicht möglich ({ex.GetType().Name}), Banner vermutlich schon zu.");
                }

                await app.SchliesseSenderBannerAsync();
                Rendezvous.Signal("t23-fertig");
            }
        }, "t23");
}
