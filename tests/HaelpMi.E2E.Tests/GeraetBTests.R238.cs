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
    /// Erster Alarm nach frischem Agent-Start, zwei Grenzen:
    /// Der Agent meldet „Popup sichtbar“ und „UI-Thread frei“ binnen <see cref="R238Latenz.MaxProdukt"/> nach dem
    /// Empfang (eigene Messung im Betriebs-Log), und das Alarmfenster ist binnen <see cref="R238Latenz.MaxBisBedienbar"/>
    /// nach „alarm received“ per UI-Automation lesbar und bedienbar (Uhr von B; enthält den ersten UIA-Aufbau).
    /// Danach „Bin unterwegs“ und schließen.
    /// </summary>
    [Fact, TestPriority(159)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R238")]
    public Task T26_Regression238_PopupBedienbarNachEmpfang() =>
        Artefakte.SichereAsync(nameof(T26_Regression238_PopupBedienbarNachEmpfang), async () =>
        {
            var b = EigeneDeviceId();
            var a = await DeviceIdDerGegenseiteAsync("a");
            var stufeVorher = R238Latenz.BetriebsLogAufDebug();
            TimeSpan bisBedienbar;
            (TimeSpan Sichtbar, TimeSpan Frei)? produkt = null;
            var geschlossen = false;
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
                var abfragen = 0;
                var laengste = TimeSpan.Zero;
                DateTimeOffset? gefundenUm = null;
                await WarteBisAsync(() =>
                    {
                        var start = DateTimeOffset.UtcNow;
                        try
                        {
                            popup ??= app.FindeAlarmfenster();
                            gefundenUm ??= popup is null ? null : DateTimeOffset.UtcNow;
                            return popup is not null && R238Latenz.Bedienbar(popup);
                        }
                        finally
                        {
                            abfragen++;
                            var dauer = DateTimeOffset.UtcNow - start;
                            laengste = dauer > laengste ? dauer : laengste;
                        }
                    },
                    R238Latenz.Suche, $"T26: Alarmfenster ist {R238Latenz.Suche.TotalSeconds:0} s nach der Marke nicht bedienbar.");
                var bedienbar = DateTimeOffset.UtcNow;

                var wellen = R136Messung.WellenSeit(a, seit);
                Assert.True(wellen.Count > 0, $"T26: audit.log enthält kein 'alarm received from deviceId={a}', obwohl das Alarmfenster offen ist.");
                bisBedienbar = bedienbar - wellen[0];
                R238Latenz.Sichere("t26-latenz-b.txt", new[] { $"UIA: Fenster gefunden {R238Latenz.Sekunden((gefundenUm ?? bedienbar) - wellen[0])}, bedienbar {R238Latenz.Sekunden(bisBedienbar)} nach 'alarm received'; {abfragen} Abfragen, längste {R238Latenz.Sekunden(laengste)}" });

                try
                {
                    await app.QuittiereUndSchliesseAsync(popup!, "T26");
                    geschlossen = true;
                }
                finally
                {
                    Rendezvous.Signal("t26-b-fertig");
                }

                await WarteBisAsync(() => (produkt = R238Latenz.ProduktZeiten(R238Latenz.BetriebsLogZeilen(), seit)) is not null,
                    R238Latenz.BetriebsLogFrist,
                    "T26: agent.log enthält für diesen Alarm kein 'Popup sichtbar' und 'UI-Thread frei' auf Stufe DEBUG (Betriebs-Log nicht auf DEBUG oder Agent ohne Zeitmessung, siehe out\\t26-betriebslog-b.txt).");
            }
            finally
            {
                R238Latenz.Sichere("t26-betriebslog-b.txt", R238Latenz.BetriebsLogZeilen());
                R238Latenz.BetriebsLogZuruecksetzen(stufeVorher);
                if (!geschlossen)
                {
                    // Ein offenes oder hängendes Alarmfenster störte die folgenden Tests; der Neustart liest auch die alte Stufe wieder.
                    BeendeAlles();
                    await StarteAgentAsync();
                    await Task.Delay(AgentAnlaufzeit);
                }
            }

            var (sichtbar, frei) = produkt!.Value;
            R238Latenz.Sichere("t26-latenz-b.txt", new[] { $"Agent: Popup sichtbar {R238Latenz.Sekunden(sichtbar)}, UI-Thread frei {R238Latenz.Sekunden(frei)} nach dem Empfang" });
            var befunde = new List<string>();
            // Getrennte Grenzen: die Produktzeit eng, die UIA-Bedienbarkeit mit Puffer für den ersten UIA-Aufbau im Agent.
            if (sichtbar > R238Latenz.MaxProdukt || frei > R238Latenz.MaxProdukt)
            {
                befunde.Add($"Regression #238: Agent meldet Popup sichtbar nach {R238Latenz.Sekunden(sichtbar)} und UI-Thread frei nach {R238Latenz.Sekunden(frei)}, erwartet je höchstens {R238Latenz.Sekunden(R238Latenz.MaxProdukt)} (out\\t26-betriebslog-b.txt).");
            }

            if (bisBedienbar > R238Latenz.MaxBisBedienbar)
            {
                befunde.Add($"Regression #238: Alarmfenster erst {R238Latenz.Sekunden(bisBedienbar)} nach dem Empfang per UI-Automation bedienbar, erwartet höchstens {R238Latenz.Sekunden(R238Latenz.MaxBisBedienbar)} (out\\t26-latenz-b.txt).");
            }

            MeldeBefunde(befunde);
        }, "t26");
}
