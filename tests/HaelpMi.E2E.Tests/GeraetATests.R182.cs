using FlaUI.Core.AutomationElements;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T13 Regressionstest #182, Gegenstück in GeraetBTests.R182.cs.
public partial class GeraetATests
{
    private static readonly TimeSpan R182Beobachtung = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Alarm A an B ohne "Bin unterwegs". Nach "Empfangen: 1 von 1" beendet B seinen Agent, weitere Wellen
    /// bleiben unquittiert. Der Zähler muss trotzdem auf 1 bleiben, B hat den Alarm ja empfangen.
    /// </summary>
    [Fact, TestPriority(110)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R182")]
    public Task T13_Regression182_ZaehlerBleibtNachEmpfang() =>
        Artefakte.SichereAsync(nameof(T13_Regression182_ZaehlerBleibtNachEmpfang), async () =>
        {
            var a = EigeneDeviceId();
            var b = await DeviceIdDerGegenseiteAsync("b");
            await BereiteAlarmprofilVorAsync(a, b, 6000);
            await Rendezvous.WaitForAsync("t11-b-bereit", GegenseiteTimeout);
            await WarteAufGeraetAsync(b, DiscoveryTimeout);

            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            DrueckeHotkey(AlarmHotkey);
            Rendezvous.Signal("t11-aus");
            var banner = app.ErwarteBanner("T13");
            try
            {
                await WarteBisAsync(() => NameVon(banner, "AckedText") == "Empfangen: 1 von 1", TimeSpan.FromSeconds(15),
                    () => $"T13: B hat die erste Welle nicht quittiert, Banner zeigt '{NameVon(banner, "AckedText")}'.");
                Rendezvous.Signal("t11-empfangen");
                await Rendezvous.WaitForAsync("t11-b-weg", SchrittTimeout);

                var seit = DateTimeOffset.UtcNow;
                var abweichend = new List<string>();
                while (DateTimeOffset.UtcNow - seit < R182Beobachtung)
                {
                    var text = NameVon(banner, "AckedText");
                    if (text != "Empfangen: 1 von 1" && !abweichend.Contains(text))
                    {
                        abweichend.Add(text);
                    }

                    await Task.Delay(200);
                }

                var wellen = AuditLogZeilen(seit).Count(z => z.Contains($"alarm sent alarmProfileId={ProfilId}", StringComparison.Ordinal));
                Assert.True(wellen >= 1,
                    $"T13: A hat nach dem Beenden des Agents auf B in {R182Beobachtung.TotalSeconds:0} s keine weitere Welle gesendet, der Fall ist nicht geprüft.");
                Assert.True(abweichend.Count == 0,
                    $"Regression #182: nach {wellen} unquittierten Wellen zeigte das Sender-Banner '{string.Join("', '", abweichend)}' statt 'Empfangen: 1 von 1'.");
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
                Rendezvous.Signal("t11-fertig");
            }
        }, "t11");
}
