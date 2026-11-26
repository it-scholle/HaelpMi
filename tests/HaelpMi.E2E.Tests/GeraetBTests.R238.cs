using FlaUI.Core.AutomationElements;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T26 Regressionstest #238, Gegenstück in GeraetATests.R238.cs.
public partial class GeraetBTests
{
    /// <summary>
    /// Erster Alarm nach frischem Agent-Start: Das Alarmfenster muss binnen <see cref="R238Latenz.MaxBisBedienbar"/>
    /// nach dem Eintrag „alarm received“ per UI-Automation lesbar und bedienbar sein. Beide Zeitpunkte stammen von
    /// der Uhr von B. Danach „Bin unterwegs“ und schließen.
    /// </summary>
    [Fact, TestPriority(159)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R238")]
    public Task T26_Regression238_PopupBedienbarNachEmpfang() =>
        Artefakte.SichereAsync(nameof(T26_Regression238_PopupBedienbarNachEmpfang), async () =>
        {
            var b = EigeneDeviceId();
            var a = await DeviceIdDerGegenseiteAsync("a");
            R238Latenz.BetriebsLogAufDebug();
            TimeSpan bisBedienbar;
            try
            {
                // Startet den Agent neu: Der Fehler trat beim ersten Empfang kurz nach dem Start auf.
                await BereiteAlarmprofilVorAsync(a, b, R238Latenz.ConfigVersion);
                await WarteAufGeraetAsync(a, DiscoveryTimeout);

                // Erster UIA-Zugriff dieses Testprozesses dauert auf einer frischen VM teils über 10 s, deshalb vor der Marke.
                using var app = new HaelpMiApp();
                Assert.False(app.AlarmfensterOffen(), "T26: Vor dem Alarm ist schon ein Alarmfenster offen.");
                var seit = DateTimeOffset.UtcNow;
                Rendezvous.Signal("t26-b-bereit");
                await Rendezvous.WaitForAsync("t26-aus", GegenseiteTimeout);

                Window? popup = null;
                await WarteBisAsync(() => (popup ??= app.FindeAlarmfenster()) is { } gefunden && R238Latenz.Bedienbar(gefunden),
                    R238Latenz.Suche, $"T26: Alarmfenster ist {R238Latenz.Suche.TotalSeconds:0} s nach der Marke nicht bedienbar.");
                var bedienbar = DateTimeOffset.UtcNow;

                var wellen = R136Messung.WellenSeit(a, seit);
                Assert.True(wellen.Count > 0, $"T26: audit.log enthält kein 'alarm received from deviceId={a}', obwohl das Alarmfenster offen ist.");
                bisBedienbar = bedienbar - wellen[0];

                try
                {
                    await app.QuittiereUndSchliesseAsync(popup!, "T26");
                }
                finally
                {
                    Rendezvous.Signal("t26-b-fertig");
                }
            }
            finally
            {
                R238Latenz.Sichere("t26-betriebslog-b.txt", R238Latenz.BetriebsLogZeilen());
                R238Latenz.BetriebsLogZuruecksetzen();
            }

            R238Latenz.Sichere("t26-latenz-b.txt", new[] { $"Alarmfenster bedienbar {R238Latenz.Sekunden(bisBedienbar)} nach 'alarm received'" });
            Assert.True(bisBedienbar <= R238Latenz.MaxBisBedienbar,
                $"Regression #238: Alarmfenster erst {R238Latenz.Sekunden(bisBedienbar)} nach dem Empfang per UI-Automation bedienbar, erwartet höchstens {R238Latenz.Sekunden(R238Latenz.MaxBisBedienbar)} (Zeitmessungen in out\\t26-betriebslog-b.txt).");
        }, "t26");
}
