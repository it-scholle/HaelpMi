using FlaUI.Core.AutomationElements;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;
using HaelpMi.Installer.Tests;
using Xunit;
using Xunit.Abstractions;
using static HaelpMi.E2E.Tests.HaelpMiApp;

namespace HaelpMi.E2E.Tests;

/// <summary>Testteil für VM A (Admin-Installer). Die Schritte bauen aufeinander auf.</summary>
[Collection(E2ESammlung.Name)]
[TestCaseOrderer("HaelpMi.Installer.Tests.PriorityOrderer", "HaelpMi.E2E.Tests")]
[Trait("Category", "E2E")]
[Trait("Geraet", "A")]
public partial class GeraetATests
{
    private static readonly TimeSpan FensterTimeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _ausgabe;

    public GeraetATests(ITestOutputHelper ausgabe) => _ausgabe = ausgabe;

    [Fact, TestPriority(10), Trait("Bereich", "Basis")]
    [Trait("FR", "FR-3"), Trait("FR", "FR-4"), Trait("FR", "FR-34"), Trait("FR", "FR-35"), Trait("FR", "FR-37"), Trait("FR", "NFR-2")]
    public Task T1_AdminInstaller_InstalliertStill() =>
        Artefakte.SichereAsync(nameof(T1_AdminInstaller_InstalliertStill),
            () => Installation.InstalliereUndPruefeAsync(Role.Admin, "A"));

    [Fact, TestPriority(20)]
    [Trait("FR", "FR-4")]
    public Task T2_VerknuepfungOeffnetDashboard_OhneAbsturz() =>
        Artefakte.SichereAsync(nameof(T2_VerknuepfungOeffnetDashboard_OhneAbsturz), async () =>
        {
            HaelpMiApp.BeendeKonfiguration();
            var crashLogVorher = CrashLogLaenge();

            using var app = new HaelpMiApp();
            var fenster = app.OeffneUeberVerknuepfung(Role.Admin, FensterTimeout);
            Assert.False(fenster.IsOffscreen, "Dashboard-Fenster ist nicht sichtbar.");

            // Fehler beim Aufbau landen über CrashLogger in crash.log, nicht als Prozessabbruch.
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.True(CrashLogLaenge() == crashLogVorher, "crash.log hat beim Öffnen des Dashboards neue Einträge bekommen.");

            HaelpMiApp.BeendeKonfiguration();
        });

    [Fact, TestPriority(30)]
    [Trait("FR", "FR-27"), Trait("FR", "FR-40"), Trait("FR", "FR-51")]
    public Task T3_Selbsttest_ZeigtAlarmfensterMitRaum() =>
        Artefakte.SichereAsync(nameof(T3_Selbsttest_ZeigtAlarmfensterMitRaum), async () =>
        {
            StelleAlarmprofilSicher();
            HaelpMiApp.BeendeKonfiguration();

            using var app = new HaelpMiApp();
            var einstellungen = app.OeffneEinstellungen(FensterTimeout);
            Element(einstellungen, "SelfTestButton").AsButton().Invoke();

            var popup = app.WarteAufAlarmfenster(FensterTimeout);
            await WarteBisAsync(() => HaelpMiApp.IstImVordergrund(popup), TimeSpan.FromSeconds(10), "Alarmfenster ist nicht im Vordergrund.");
            Assert.Equal($"{E2EUmgebung.Raum} ({E2EUmgebung.Raumnummer})", Element(popup, "RoomText").Name);

            // Beim Selbsttest ist die Schwelle immer 1 (AlarmFlowCoordinator.SendSelfTestAsync).
            Element(popup, "OnMyWayButton").AsButton().Invoke();
            var schliessen = Element(popup, "CloseButton").AsButton();
            await WarteBisAsync(() => schliessen.IsEnabled, TimeSpan.FromSeconds(15), "Schließen-Knopf wird nach 'Bin unterwegs' nicht aktiv.");
            schliessen.Invoke();

            await WarteBisAsync(() => !app.AlarmfensterOffen(), TimeSpan.FromSeconds(10), "Alarmfenster ist nach dem Schließen noch offen.");
            HaelpMiApp.BeendeKonfiguration();
        });

    [Fact, TestPriority(40), Trait("Bereich", "Basis")]
    [Trait("Teil", ZweiGeraete.Teil)]
    public Task T4_Discovery_AKenntB() =>
        Artefakte.SichereAsync(nameof(T4_Discovery_AKenntB), async () =>
        {
            var b = await ZweiGeraete.DeviceIdDerGegenseiteAsync("b");
            var eintrag = await ZweiGeraete.WarteAufGeraetAsync(b, ZweiGeraete.DiscoveryTimeout);
            _ausgabe.WriteLine($"B ({b}) in devices.json, Rolle {eintrag.Role}, Raum '{eintrag.RoomName}'.");
        });

    // Gegenstück: GeraetBTests.T5_Alarm_VonA_MitRueckmeldung.
    [Fact, TestPriority(50)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("FR", "FR-50"), Trait("FR", "FR-53")]
    public Task T5_Alarm_AnB_MitRueckmeldung() =>
        Artefakte.SichereAsync(nameof(T5_Alarm_AnB_MitRueckmeldung), async () =>
        {
            var a = ZweiGeraete.EigeneDeviceId();
            var b = await ZweiGeraete.DeviceIdDerGegenseiteAsync("b");
            await ZweiGeraete.BereiteAlarmprofilVorAsync(a, b);

            var raumB = (await Rendezvous.WaitForAsync("b-bereit", ZweiGeraete.GegenseiteTimeout)).Trim();
            await ZweiGeraete.WarteAufGeraetAsync(b, ZweiGeraete.DiscoveryTimeout);

            using var app = new HaelpMiApp();
            DrueckeHotkey(ZweiGeraete.AlarmHotkey);
            Rendezvous.Signal("a-ausgeloest", $"{E2EUmgebung.Raum} ({E2EUmgebung.Raumnummer})\n{DateTimeOffset.UtcNow:O}");

            Window banner;
            try
            {
                banner = app.WarteAufSenderBanner(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                // AlarmFlowCoordinator.TriggerAlarmProfile zeigt nichts, wenn nach Lizenzfilter kein Empfänger bleibt.
                Assert.Fail("Kein Sender-Banner nach dem Hotkey: Hotkey nicht registriert, A lizenzgesperrt oder B kein gültiger Empfänger.");
                throw;
            }

            await Rendezvous.WaitForAsync("b-quittiert", TimeSpan.FromMinutes(2));
            await WarteBisAsync(() => AufDemWeg(banner).Any(n => n.StartsWith(raumB + " - ", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(15), $"'Bin unterwegs' von B (Raum '{raumB}') erscheint nicht unter 'Auf dem Weg'.");
            Assert.Equal("Empfangen: 1 von 1", Element(banner, "AckedText").Name);
        });

    // Ein frisch installiertes Gerät hat noch kein Alarm-Profil, ohne eines lehnt der Agent den Selbsttest ab.
    // Profil anlegen über das Dashboard ist Gegenstand späterer Tests, hier genügt der Datenstand.
    private static void StelleAlarmprofilSicher()
    {
        var store = new SharedConfigStore();
        var config = store.LoadOrCreate();
        if (config.AlarmProfiles.Count > 0)
        {
            return;
        }

        config.AlarmProfiles.Add(new AlarmProfile { Name = "E2E-Selbsttest", Text = "E2E-Selbsttest" });
        store.Save(config);
    }

    // Die ItemsControl-Einträge sind Strings, UIA liefert sie als Namen der Kind-Elemente.
    private static List<string> AufDemWeg(Window banner)
    {
        try
        {
            return Element(banner, "OnTheWayList").FindAllDescendants()
                .Select(e => e.Properties.Name.ValueOrDefault ?? string.Empty)
                .ToList();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return new List<string>();
        }
    }

    private static long CrashLogLaenge() => E2EUmgebung.LogOrdner()
        .Select(o => new FileInfo(Path.Combine(o, "crash.log")))
        .Sum(f => SichereLaenge(f));

    private static long SichereLaenge(FileInfo datei)
    {
        try
        {
            return datei.Exists ? datei.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
