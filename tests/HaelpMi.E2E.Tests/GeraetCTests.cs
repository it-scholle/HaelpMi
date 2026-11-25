using FlaUI.Core.AutomationElements;
using HaelpMi.Core.Models;
using HaelpMi.Installer.Tests;
using Xunit;
using Xunit.Abstractions;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

/// <summary>
/// Testteil für VM C, ein zweites User-Gerät neben B. Nach T12, T18 und T19 beendet C seinen Agent: sonst verteilte C in T9
/// seine Lizenz per Gossip an A und B (TryAdoptFromPeer) und wäre in T6 bis T10 ein unbeteiligter, aber
/// antwortender Peer.
/// </summary>
[Collection(E2ESammlung.Name)]
[TestCaseOrderer("HaelpMi.Installer.Tests.PriorityOrderer", "HaelpMi.E2E.Tests")]
[Trait("Category", "E2E")]
[Trait("Geraet", "C")]
public class GeraetCTests
{
    private readonly ITestOutputHelper _ausgabe;

    public GeraetCTests(ITestOutputHelper ausgabe) => _ausgabe = ausgabe;

    [Fact, TestPriority(10), Trait("Bereich", "Basis")]
    [Trait("FR", "FR-3"), Trait("FR", "FR-4"), Trait("FR", "FR-34"), Trait("FR", "FR-35"), Trait("FR", "FR-37"), Trait("FR", "NFR-2")]
    public Task T1_UserInstaller_InstalliertStill() =>
        Artefakte.SichereAsync(nameof(T1_UserInstaller_InstalliertStill),
            () => Installation.InstalliereUndPruefeAsync(Role.User, "C"));

    [Fact, TestPriority(53), Trait("Bereich", "Basis")]
    [Trait("Teil", DreiGeraete.Teil)]
    public Task T11_Discovery_CKenntAUndB() =>
        Artefakte.SichereAsync(nameof(T11_Discovery_CKenntAUndB), async () =>
        {
            foreach (var geraet in new[] { "a", "b" })
            {
                var id = await DeviceIdDerGegenseiteAsync(geraet);
                var eintrag = await WarteAufGeraetAsync(id, DiscoveryTimeout);
                _ausgabe.WriteLine($"{geraet.ToUpperInvariant()} ({id}) in devices.json, Rolle {eintrag.Role}, Raum '{eintrag.RoomName}'.");
            }
        });

    // Gegenstücke: GeraetATests.T12_Alarm_AnBUndC_Schwelle2, GeraetBTests.T12_Alarm_VonA_ErsteRueckmeldung.
    [Fact, TestPriority(56), Trait("Bereich", "Basis")]
    [Trait("Teil", DreiGeraete.Teil), Trait("FR", "FR-47"), Trait("FR", "FR-51")]
    public async Task T12_Alarm_VonA_ZweiteRueckmeldungGibtFrei()
    {
        try
        {
            await Artefakte.SichereAsync(nameof(T12_Alarm_VonA_ZweiteRueckmeldungGibtFrei), async () =>
            {
                var befunde = new List<string>();
                await DreiGeraete.BereiteAlarmprofilVorAsync();

                using var app = new HaelpMiApp();
                Assert.False(app.AlarmfensterOffen(), "Vor dem Alarm ist auf C schon ein Alarmfenster offen.");
                Rendezvous.Signal("t12-c-bereit", E2EUmgebung.Raum);

                var raumA = (await Rendezvous.WaitForAsync("t12-ausgeloest", GegenseiteTimeout)).Split('\n')[0].Trim();
                var popup = app.ErwartePopup("T12: Alarm von A auf C");
                Assert.Equal(raumA, Element(popup, "RoomText").Name);
                var schliessen = Element(popup, "CloseButton").AsButton();
                Assert.False(schliessen.IsEnabled, "Schließen ist auf C vor jeder Rückmeldung frei (Schwelle 2).");

                // B meldet zuerst; A gibt den Zählerstand an alle Empfänger weiter (AlarmStatusRelayMessage).
                await Rendezvous.WaitForAsync("t12-b-unterwegs", SchrittTimeout);
                await PruefeBefundAsync(befunde, () => WarteBisAsync(() => NameVon(popup, "ThresholdStatusText").Contains("noch 1", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(15),
                    () => $"Produktbefund: C zeigt nach 'Bin unterwegs' auf B '{NameVon(popup, "ThresholdStatusText")}' statt '… noch 1 …' (Rückmeldung von B kommt nicht über A bei C an)."));
                _ausgabe.WriteLine($"C nach der Rückmeldung von B: '{NameVon(popup, "ThresholdStatusText")}'");
                Assert.False(schliessen.IsEnabled, $"Schließen ist auf C nach einer von zwei Rückmeldungen frei ({NameVon(popup, "ThresholdStatusText")}).");

                await Rendezvous.WaitForAsync("t12-b-gesperrt", SchrittTimeout);
                Element(popup, "OnMyWayButton").AsButton().Invoke();
                Rendezvous.Signal("t12-c-unterwegs");
                await WarteBisAsync(() => schliessen.IsEnabled, TimeSpan.FromSeconds(15),
                    () => $"Schließen wird auf C nach der zweiten Rückmeldung nicht frei ({NameVon(popup, "ThresholdStatusText")}).");
                schliessen.Invoke();
                await WarteBisAsync(() => !app.AlarmfensterOffen(), TimeSpan.FromSeconds(10), "Alarmfenster ist auf C nach dem Schließen noch offen.");
                await Rendezvous.WaitForAsync("t12-a-fertig", SchrittTimeout);
                MeldeBefunde(befunde);
            }, "t12");
        }
        finally
        {
            // Erst nach SichereAsync, damit der Screenshot eines Fehlschlags das Alarmfenster noch zeigt.
            BeendeAlles();
            Rendezvous.Signal(DreiGeraete.CGestoppt);
        }
    }

    // Gegenstücke: GeraetATests.T18_Regression136_DreiGeraete_SendetWellen, GeraetBTests.T18_Regression136_DreiGeraete_FensterBleibtZu.
    // Startet den nach T12 beendeten Agent über die Profilvorbereitung neu und beendet ihn danach wieder.
    [Fact, TestPriority(57)]
    [Trait("Teil", DreiGeraete.Teil), Trait("Bereich", "R136")]
    public async Task T18_Regression136_DreiGeraete_FensterBleibtZu()
    {
        try
        {
            await Artefakte.SichereAsync(nameof(T18_Regression136_DreiGeraete_FensterBleibtZu),
                () => R136Messung.DreiGeraeteEmpfaengerAsync("c", _ausgabe), "t18");
        }
        finally
        {
            BeendeAlles();
            Rendezvous.Signal(DreiGeraete.CGestopptT18);
        }
    }

    // Gegenstücke: GeraetATests.T19_Regression136_MehrfachNachwelle_SendetWellen, GeraetBTests.T19_Regression136_MehrfachNachwelle_EinFenster.
    // C zählt nur und klickt nicht nach; startet den Agent wie T18 über die Profilvorbereitung und beendet ihn danach.
    [Fact, TestPriority(58)]
    [Trait("Teil", DreiGeraete.Teil), Trait("Teil", R136Mehrfach.Teil), Trait("Bereich", "R136")]
    public async Task T19_Regression136_MehrfachNachwelle_EinFenster()
    {
        try
        {
            await Artefakte.SichereAsync(nameof(T19_Regression136_MehrfachNachwelle_EinFenster),
                () => R136Mehrfach.EmpfaengerAsync("c", klickmuster: false, _ausgabe), "t19");
        }
        finally
        {
            BeendeAlles();
            Rendezvous.Signal(DreiGeraete.CGestopptT19);
        }
    }
}
