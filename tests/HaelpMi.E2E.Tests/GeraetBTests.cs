using System.Globalization;
using FlaUI.Core.AutomationElements;
using HaelpMi.Core.Models;
using HaelpMi.Installer.Tests;
using Xunit;
using Xunit.Abstractions;
using static HaelpMi.E2E.Tests.HaelpMiApp;

namespace HaelpMi.E2E.Tests;

/// <summary>Testteil für VM B (User-Installer).</summary>
[Collection(E2ESammlung.Name)]
[TestCaseOrderer("HaelpMi.Installer.Tests.PriorityOrderer", "HaelpMi.E2E.Tests")]
[Trait("Category", "E2E")]
[Trait("Geraet", "B")]
public partial class GeraetBTests
{
    private static readonly TimeSpan PopupTimeout = TimeSpan.FromSeconds(15);

    private readonly ITestOutputHelper _ausgabe;

    public GeraetBTests(ITestOutputHelper ausgabe) => _ausgabe = ausgabe;

    [Fact, TestPriority(10), Trait("Bereich", "Basis")]
    [Trait("FR", "FR-3"), Trait("FR", "FR-4"), Trait("FR", "FR-34"), Trait("FR", "FR-35"), Trait("FR", "FR-37"), Trait("FR", "NFR-2")]
    public Task T1_UserInstaller_InstalliertStill() =>
        Artefakte.SichereAsync(nameof(T1_UserInstaller_InstalliertStill),
            () => Installation.InstalliereUndPruefeAsync(Role.User, "B"));

    [Fact, TestPriority(40), Trait("Bereich", "Basis")]
    [Trait("Teil", ZweiGeraete.Teil)]
    public Task T4_Discovery_BKenntA() =>
        Artefakte.SichereAsync(nameof(T4_Discovery_BKenntA), async () =>
        {
            var a = await ZweiGeraete.DeviceIdDerGegenseiteAsync("a");
            var eintrag = await ZweiGeraete.WarteAufGeraetAsync(a, ZweiGeraete.DiscoveryTimeout);
            _ausgabe.WriteLine($"A ({a}) in devices.json, Rolle {eintrag.Role}, Raum '{eintrag.RoomName}'.");
        });

    // Gegenstück: GeraetATests.T5_Alarm_AnB_MitRueckmeldung.
    [Fact, TestPriority(50), Trait("Bereich", "Basis")]
    [Trait("Teil", ZweiGeraete.Teil), Trait("FR", "FR-40"), Trait("FR", "FR-51"), Trait("FR", "FR-53")]
    public Task T5_Alarm_VonA_MitRueckmeldung() =>
        Artefakte.SichereAsync(nameof(T5_Alarm_VonA_MitRueckmeldung), async () =>
        {
            var b = ZweiGeraete.EigeneDeviceId();
            var a = await ZweiGeraete.DeviceIdDerGegenseiteAsync("a");

            // Zeitmessungen im Empfangspfad (#238) landen so im agent.log des Artefakts. Die Stufe wird beim
            // Agent-Start gelesen; der Neustart steckt in BereiteAlarmprofilVorAsync, danach gilt wieder die alte.
            var stufeVorher = R238Latenz.BetriebsLogAufDebug();
            try
            {
                await ZweiGeraete.BereiteAlarmprofilVorAsync(a, b);
            }
            finally
            {
                R238Latenz.BetriebsLogZuruecksetzen(stufeVorher);
            }

            // Erster UIA-Zugriff dieses Testprozesses dauert auf einer frischen VM teils über 10 s, deshalb vor der Marke.
            using var app = new HaelpMiApp();
            Assert.False(app.AlarmfensterOffen(), "Vor dem Alarm ist schon ein Alarmfenster offen.");
            Rendezvous.Signal("b-bereit", E2EUmgebung.Raum);

            // Inhalt: Zeile 1 Raumtext von A, Zeile 2 Auslösezeitpunkt (UTC, Uhr von A).
            var zeilen = (await Rendezvous.WaitForAsync("a-ausgeloest", ZweiGeraete.GegenseiteTimeout)).Split('\n');
            var markeDa = DateTimeOffset.UtcNow;

            Window popup;
            try
            {
                popup = app.WarteAufAlarmfenster(PopupTimeout);
            }
            catch (TimeoutException ex)
            {
                // netzwerk.txt im Artefakt zeigt die Firewall-Regeln und ob 51501 lauscht.
                Assert.Fail($"Kein Alarmfenster {PopupTimeout.TotalSeconds:0} s nach der Marke a-ausgeloest ({ex.Message}).");
                throw;
            }

            var popupDa = DateTimeOffset.UtcNow;
            var latenz = $"Alarmfenster {(popupDa - markeDa).TotalSeconds:0.0} s nach Eintreffen der Marke";
            if (zeilen.Length > 1 && DateTimeOffset.TryParse(zeilen[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ausgeloest))
            {
                latenz += $", {(popupDa - ausgeloest).TotalSeconds:0.0} s nach dem Hotkey auf A (Uhren nicht abgeglichen)";
            }
            _ausgabe.WriteLine(latenz);
            Directory.CreateDirectory(Artefakte.Ordner);
            File.AppendAllText(Path.Combine(Artefakte.Ordner, "t5-latenz-b.txt"), latenz + Environment.NewLine);

            Assert.Equal(zeilen[0].Trim(), Element(popup, "RoomText").Name);

            Element(popup, "OnMyWayButton").AsButton().Invoke();
            Rendezvous.Signal("b-quittiert");

            // Schwelle 1 erreicht: A meldet das zurück, erst dann wird Schließen aktiv (#136).
            var schliessen = Element(popup, "CloseButton").AsButton();
            await WarteBisAsync(() => schliessen.IsEnabled, TimeSpan.FromSeconds(15), "Schließen-Knopf wird nach 'Bin unterwegs' nicht aktiv (Rückmeldung von A fehlt).");
            schliessen.Invoke();
            await WarteBisAsync(() => !app.AlarmfensterOffen(), TimeSpan.FromSeconds(10), "Alarmfenster ist nach dem Schließen noch offen.");
        });
}
