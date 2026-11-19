using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T11 Regressionstest #182, Auswertung in GeraetATests.R182.cs.
public partial class GeraetBTests
{
    /// <summary>Empfängt die erste Welle, beendet dann den Agent, damit A keine weitere Quittung bekommt, und startet ihn danach wieder.</summary>
    [Fact, TestPriority(110)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R182")]
    public Task T11_Regression182_QuittiertNurErsteWelle() =>
        Artefakte.SichereAsync(nameof(T11_Regression182_QuittiertNurErsteWelle), async () =>
        {
            var b = EigeneDeviceId();
            var a = await DeviceIdDerGegenseiteAsync("a");
            await BereiteAlarmprofilVorAsync(a, b, 6000);
            await WarteAufGeraetAsync(a, DiscoveryTimeout);
            Rendezvous.Signal("t11-b-bereit");

            using var app = new HaelpMiApp();
            await Rendezvous.WaitForAsync("t11-aus", GegenseiteTimeout);
            app.ErwartePopup("T11");
            try
            {
                await Rendezvous.WaitForAsync("t11-empfangen", SchrittTimeout);
            }
            finally
            {
                BeendeAlles();
                Rendezvous.Signal("t11-b-weg");
            }

            await Rendezvous.WaitForAsync("t11-fertig", SchrittTimeout);
            await StarteAgentAsync();
            await Task.Delay(AgentAnlaufzeit);
        }, "t11");
}
