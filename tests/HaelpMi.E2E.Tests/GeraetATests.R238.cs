using FlaUI.Core.AutomationElements;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T26 Regressionstest #238, Gegenstück in GeraetBTests.R238.cs.
public partial class GeraetATests
{
    /// <summary>Alarm A an B, kurz nach dem Agent-Start auf B. A muss B binnen <see cref="ZweiGeraete.QuittungFrist"/> als empfangen zählen.</summary>
    [Fact, TestPriority(159)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R238")]
    public Task T26_Regression238_QuittungKurzNachStart() =>
        Artefakte.SichereAsync(nameof(T26_Regression238_QuittungKurzNachStart), async () =>
        {
            var a = EigeneDeviceId();
            var b = await DeviceIdDerGegenseiteAsync("b");
            await BereiteAlarmprofilVorAsync(a, b, R238Latenz.ConfigVersion);
            await Rendezvous.WaitForAsync("t26-b-bereit", GegenseiteTimeout);
            await WarteAufGeraetAsync(b, DiscoveryTimeout);

            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            var ausgeloest = DateTimeOffset.UtcNow;
            DrueckeHotkey(AlarmHotkey);
            Rendezvous.Signal("t26-aus");
            var banner = app.ErwarteBanner("T26");

            var befunde = new List<string>();
            try
            {
                await PruefeBefundAsync(befunde, () => WarteAufQuittungAsync(banner, ausgeloest, "t26-latenz-a.txt"));
                await Rendezvous.WaitForAsync("t26-b-fertig", SchrittTimeout);
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
            }

            MeldeBefunde(befunde);
        }, "t26");

    /// <summary>Wartet, bis das Banner „Empfangen: 1 von 1“ zeigt, und hält die Zeit seit dem Hotkey fest.</summary>
    internal static async Task WarteAufQuittungAsync(Window banner, DateTimeOffset ausgeloest, string artefakt)
    {
        try
        {
            await WarteBisAsync(() => NameVon(banner, "AckedText") == "Empfangen: 1 von 1",
                QuittungFrist - (DateTimeOffset.UtcNow - ausgeloest),
                () => $"Regression #238: A zeigt {QuittungFrist.TotalSeconds:0} s nach dem Hotkey '{NameVon(banner, "AckedText")}' statt 'Empfangen: 1 von 1' (Quittung von B fehlt, siehe audit.log und agent.log von B).");
        }
        finally
        {
            R238Latenz.Sichere(artefakt, new[] { $"'{NameVon(banner, "AckedText")}' {R238Latenz.Sekunden(DateTimeOffset.UtcNow - ausgeloest)} nach dem Hotkey" });
        }
    }
}
