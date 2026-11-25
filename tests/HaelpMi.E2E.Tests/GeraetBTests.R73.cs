using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T23 Regressionstest #73, Auswertung in GeraetATests.R73.cs.
public partial class GeraetBTests
{
    /// <summary>Beendet den Agent, bevor A auslöst, startet ihn während des laufenden Alarms und erwartet das Alarmfenster.</summary>
    [Fact, TestPriority(170)]
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

            await Rendezvous.WaitForAsync("t23-aus", GegenseiteTimeout);
            using var app = new HaelpMiApp();
            try
            {
                await StarteAgentAsync();
                app.ErwartePopup("T23");
            }
            finally
            {
                await Rendezvous.WaitForAsync("t23-fertig", SchrittTimeout);
                BeendeAlles();
                await StarteAgentAsync();
                await Task.Delay(AgentAnlaufzeit);
            }
        }, "t23");
}
