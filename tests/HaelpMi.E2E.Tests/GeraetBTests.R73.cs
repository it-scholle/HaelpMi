using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T23, Auswertung in GeraetATests.R73.cs.
public partial class GeraetBTests
{
    /// <summary>Beendet den Agent, bevor A auslöst, startet ihn während des laufenden Alarms und erwartet das Alarmfenster.</summary>
    [Fact, TestPriority(158)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R73")]
    public Task T23_Regression73_StartetWaehrendAlarm() =>
        Artefakte.SichereAsync(nameof(T23_Regression73_StartetWaehrendAlarm), async () =>
        {
            var b = EigeneDeviceId();
            var a = await DeviceIdDerGegenseiteAsync("a");
            await BereiteAlarmprofilVorAsync(a, b, 7000);
            await WarteAufGeraetAsync(a, DiscoveryTimeout);
            Rendezvous.Signal("t23-b-bereit");

            BeendeAlles();
            Rendezvous.Signal("t23-b-weg");

            using var app = new HaelpMiApp();
            try
            {
                await Rendezvous.WaitForAsync("t23-aus", GegenseiteTimeout);
                await StarteAgentAsync();
                // Eine Welle an die veraltete Adresse dauert auf A mit Zeitlimits bis etwa 15 s, erst die nächste liest nach.
                app.ErwartePopup("T23", TimeSpan.FromSeconds(45));
            }
            finally
            {
                // Auch wenn A gescheitert ist: B startet neu, damit A per Boot-Call wieder die richtige Adresse kennt.
                try
                {
                    await Rendezvous.WaitForAsync("t23-fertig", SchrittTimeout);
                }
                catch (TimeoutException)
                {
                }

                BeendeAlles();
                await StarteAgentAsync();
                await Task.Delay(AgentAnlaufzeit);
            }
        }, "t23");
}
