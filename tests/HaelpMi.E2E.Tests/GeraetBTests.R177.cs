using System.Diagnostics;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T20 Regressionstest #177, Gegenstück in GeraetATests.R177.cs.
public partial class GeraetBTests
{
    /// <summary>
    /// Erster Alarm nach frischem Agent-Start: Die ersten Wellen müssen im 5-s-Takt ankommen und das Alarmfenster muss
    /// UI-Automation sofort antworten. Danach beendet B seinen Agent, damit A den Takt ohne Quittung prüfen kann.
    /// </summary>
    [Fact, TestPriority(115)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R177")]
    public Task T20_Regression177_TaktAbErstemAlarm() =>
        Artefakte.SichereAsync(nameof(T20_Regression177_TaktAbErstemAlarm), async () =>
        {
            var b = EigeneDeviceId();
            var a = await DeviceIdDerGegenseiteAsync("a");
            // Startet den Agent neu, der folgende Alarm ist also der erste Empfang dieses Prozesses.
            await BereiteAlarmprofilVorAsync(a, b, R177Takt.ConfigVersion);
            await WarteAufGeraetAsync(a, DiscoveryTimeout);

            var seit = DateTimeOffset.UtcNow;
            Rendezvous.Signal("t20-b-bereit");
            TimeSpan uia;
            string uiaText;
            List<DateTimeOffset> wellen;
            using (var app = new HaelpMiApp())
            {
                await Rendezvous.WaitForAsync("t20-aus", GegenseiteTimeout);
                try
                {
                    var popup = app.ErwartePopup("T20");
                    var messung = Stopwatch.StartNew();
                    uiaText = NameVon(popup, "ThresholdStatusText");
                    uia = messung.Elapsed;
                    wellen = await R136Messung.WarteAufWellenAsync(a, seit, 3);
                }
                finally
                {
                    BeendeAlles();
                    Rendezvous.Signal("t20-b-weg");
                }
            }

            try
            {
                await Rendezvous.WaitForAsync("t20-fertig", SchrittTimeout);
            }
            finally
            {
                // Auch wenn A scheitert: ohne Agent auf B liefen T14 bis T17 ins Leere.
                await StarteAgentAsync();
                await Task.Delay(AgentAnlaufzeit);
            }

            var abstaende = R177Takt.Abstaende(wellen.Take(3).ToList());
            R177Takt.Sichere("t20-takt-b.txt",
                $"Wellenabstände ab dem ersten Empfang: {R177Takt.Text(abstaende)}; UIA-Abfrage am Alarmfenster: {uia.TotalMilliseconds:0} ms");
            Assert.True(abstaende.All(x => x <= R177Takt.MaxAbstand),
                $"Regression #177: Wellenabstände nach dem ersten Empfang {R177Takt.Text(abstaende)}, erwartet höchstens {R177Takt.MaxAbstand.TotalSeconds:0} s.");
            Assert.False(string.IsNullOrEmpty(uiaText), "T20: Die UIA-Abfrage am Alarmfenster lieferte keinen Text, die Messung ist ungültig.");
            Assert.True(uia <= R177Takt.MaxUiaAntwort,
                $"Regression #177: UI-Automation-Abfrage am frisch geöffneten Alarmfenster dauerte {uia.TotalMilliseconds:0} ms.");
        }, "t20");
}
