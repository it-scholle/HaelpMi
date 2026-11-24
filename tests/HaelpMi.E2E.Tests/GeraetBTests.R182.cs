using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T13 Regressionstest #182, Auswertung in GeraetATests.R182.cs.
public partial class GeraetBTests
{
    /// <summary>Empfängt die erste Welle, beendet dann den Agent, damit A keine weitere Quittung bekommt, und startet ihn danach wieder.</summary>
    [Fact, TestPriority(110)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R182")]
    public Task T13_Regression182_QuittiertNurErsteWelle() =>
        Artefakte.SichereAsync(nameof(T13_Regression182_QuittiertNurErsteWelle), async () =>
        {
            var b = EigeneDeviceId();
            var a = await DeviceIdDerGegenseiteAsync("a");
            await BereiteAlarmprofilVorAsync(a, b, 6000);
            await WarteAufGeraetAsync(a, DiscoveryTimeout);
            Rendezvous.Signal("t13-b-bereit");

            using var app = new HaelpMiApp();
            await Rendezvous.WaitForAsync("t13-aus", GegenseiteTimeout);
            app.ErwartePopup("T13");
            try
            {
                await Rendezvous.WaitForAsync("t13-empfangen", SchrittTimeout);
            }
            finally
            {
                BeendeAlles();
                Rendezvous.Signal("t13-b-weg");
            }

            await Rendezvous.WaitForAsync("t13-fertig", SchrittTimeout);
            await StarteAgentAsync();
            await Task.Delay(AgentAnlaufzeit);
        }, "t13");
}
