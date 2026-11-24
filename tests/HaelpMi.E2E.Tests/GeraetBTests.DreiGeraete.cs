using FlaUI.Core.AutomationElements;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T11/T12 mit drei Geräten, Gegenstücke in GeraetATests.DreiGeraete.cs und GeraetCTests.cs; T14 in GeraetBTests.R136.cs.
public partial class GeraetBTests
{
    [Fact, TestPriority(53), Trait("Bereich", "Basis")]
    [Trait("Teil", DreiGeraete.Teil)]
    public Task T11_Discovery_BKenntC() =>
        Artefakte.SichereAsync(nameof(T11_Discovery_BKenntC), async () =>
        {
            var c = await DeviceIdDerGegenseiteAsync("c");
            var eintrag = await WarteAufGeraetAsync(c, DiscoveryTimeout);
            _ausgabe.WriteLine($"C ({c}) in devices.json, Rolle {eintrag.Role}, Raum '{eintrag.RoomName}'.");
        });

    // B meldet zuerst "Bin unterwegs": bei Schwelle 2 bleibt Schließen gesperrt, bis auch C gemeldet hat.
    [Fact, TestPriority(56), Trait("Bereich", "Basis")]
    [Trait("Teil", DreiGeraete.Teil), Trait("FR", "FR-47"), Trait("FR", "FR-51")]
    public async Task T12_Alarm_VonA_ErsteRueckmeldung()
    {
        try
        {
            await T12_Rumpf();
        }
        catch
        {
            // Erst nach SichereAsync, damit der Screenshot eines Fehlschlags das Alarmfenster noch zeigt.
            await RaeumeAlarmfensterAufAsync("T12");
            throw;
        }
    }

    private Task T12_Rumpf() =>
        Artefakte.SichereAsync(nameof(T12_Alarm_VonA_ErsteRueckmeldung), async () =>
        {
            await DreiGeraete.BereiteAlarmprofilVorAsync();

            using var app = new HaelpMiApp();
            Assert.False(app.AlarmfensterOffen(), "Vor dem Alarm ist auf B schon ein Alarmfenster offen.");
            Rendezvous.Signal("t12-b-bereit", E2EUmgebung.Raum);

            var raumA = (await Rendezvous.WaitForAsync("t12-ausgeloest", GegenseiteTimeout)).Split('\n')[0].Trim();
            var popup = app.ErwartePopup("T12: Alarm von A auf B");
            Assert.Equal(raumA, Element(popup, "RoomText").Name);
            var schliessen = Element(popup, "CloseButton").AsButton();
            Assert.False(schliessen.IsEnabled, "Schließen ist auf B vor jeder Rückmeldung frei (Schwelle 2).");

            Element(popup, "OnMyWayButton").AsButton().Invoke();
            Rendezvous.Signal("t12-b-unterwegs");
            var bis = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < bis)
            {
                Assert.False(schliessen.IsEnabled, $"Schließen ist auf B nach einer von zwei Rückmeldungen frei ({NameVon(popup, "ThresholdStatusText")}).");
                await Task.Delay(300);
            }

            _ausgabe.WriteLine($"B nach der eigenen Rückmeldung: '{NameVon(popup, "ThresholdStatusText")}'");
            Rendezvous.Signal("t12-b-gesperrt");

            await Rendezvous.WaitForAsync("t12-c-unterwegs", SchrittTimeout);
            await WarteBisAsync(() => schliessen.IsEnabled, TimeSpan.FromSeconds(15),
                () => $"Schließen wird auf B nach der Rückmeldung von C nicht frei ({NameVon(popup, "ThresholdStatusText")}).");
            schliessen.Invoke();
            await WarteBisAsync(() => !app.AlarmfensterOffen(), TimeSpan.FromSeconds(10), "Alarmfenster ist auf B nach dem Schließen noch offen.");
        }, "t12");

    // Ein offenes Alarmfenster stört T6. A bricht seinen Alarm beim eigenen Fehlschlag ab, das Fenster kann also
    // noch von selbst zugehen; sonst bleibt Schließen bei Schwelle 2 gesperrt und nur ein Agent-Neustart hilft.
    private async Task RaeumeAlarmfensterAufAsync(string fall)
    {
        try
        {
            using var app = new HaelpMiApp();
            var frist = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (app.AlarmfensterOffen() && DateTime.UtcNow < frist)
            {
                await Task.Delay(500);
            }

            if (app.AlarmfensterOffen())
            {
                _ausgabe.WriteLine($"{fall}: Alarmfenster auf B nach dem Fehlschlag noch offen, Agent wird neu gestartet.");
                await AgentNeustartMitAnlaufAsync();
            }
        }
        catch (Exception ex)
        {
            // Das Aufräumen darf den eigentlichen Testfehler nicht überdecken.
            _ausgabe.WriteLine($"{fall}: Aufräumen auf B fehlgeschlagen: {ex.Message}");
        }
    }
}
