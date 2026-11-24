using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// Hauptfunktionen (#171), Gegenstücke in GeraetATests.Haupt.cs.
public partial class GeraetBTests
{
    private static readonly TimeSpan NegativDauer = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(15);

    [Fact, TestPriority(60)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "Haupt"), Trait("FR", "FR-50"), Trait("FR", "FR-51")]
    public Task T6_ZweiAlarmprofile_NurRichtigeEmpfaenger() =>
        Artefakte.SichereAsync(nameof(T6_ZweiAlarmprofile_NurRichtigeEmpfaenger), async () =>
        {
            var b = EigeneDeviceId();
            var a = await DeviceIdDerGegenseiteAsync("a");
            var ids = (await Rendezvous.WaitForAsync("t6-profile-da", GegenseiteTimeout)).Split('\n').Select(z => Guid.Parse(z.Trim())).ToArray();
            var (p1, p2) = (ids[0], ids[1]);

            var dauer = await WarteMitDauerAsync(() => ProfileAngekommen(p1, p2, a, b), TimeSpan.FromSeconds(60),
                () => $"Die Profile {P1Name}/{P2Name} kommen nicht vollständig per Config-Sync auf B an (shared-config.json/AppliedConfigVersion).");
            _ausgabe.WriteLine($"Profile aus dem Dashboard {dauer.TotalSeconds:0.0} s nach der Marke auf B angewendet.");
            // Hotkeys registriert der Agent erst im ConfigApplied-Handler, kurz nach dem Schreiben der Dateien.
            await Task.Delay(TimeSpan.FromSeconds(2));
            Rendezvous.Signal("t6-b-bereit");

            using var app = new HaelpMiApp();
            await Rendezvous.WaitForAsync("t6-p1-aus", SchrittTimeout);
            var popup = app.ErwartePopup("P1 von A");
            Assert.Contains("noch 1", NameVon(popup, "ThresholdStatusText"));
            Element(popup, "OnMyWayButton").AsButton().Invoke();
            Rendezvous.Signal("t6-b-p1-quittiert");
            var schliessen = Element(popup, "CloseButton").AsButton();
            await WarteBisAsync(() => schliessen.IsEnabled, TimeSpan.FromSeconds(15),
                () => $"P1: Schließen wird nach 'Bin unterwegs' nicht frei ({NameVon(popup, "ThresholdStatusText")}).");
            schliessen.Invoke();
            await WarteBisAsync(() => !app.AlarmfensterOffen(), TimeSpan.FromSeconds(10), "P1: Alarmfenster ist nach dem Schließen noch offen.");

            await KeinFensterBisMarkeAsync(app.FindeAlarmfenster, "t6-p2a-aus", SchrittTimeout, NegativDauer,
                "P2 auf A ausgelöst: A ist dort kein Sender, trotzdem erscheint auf B ein Alarmfenster.");

            DrueckeHotkey(HotkeyP2);
            Rendezvous.Signal("t6-p2b-aus");
            var banner = app.ErwarteBanner("P2 von B");
            await Rendezvous.WaitForAsync("t6-a-unterwegs", SchrittTimeout);
            Element(banner, "CancelButton").AsButton().Invoke();
            Rendezvous.Signal("t6-b-abgebrochen");

            await Rendezvous.WaitForAsync("t6-a-p2-fertig", SchrittTimeout);
            await app.SchliesseSenderBannerAsync();
            DrueckeHotkey(HotkeyP1);
            Rendezvous.Signal("t6-b-p1-aus");
            await WarteKeinFensterAsync(app.FindeSenderBanner, TimeSpan.FromSeconds(5), "P1 auf B ausgelöst: B ist dort kein Sender, trotzdem erscheint ein Banner.");
        }, "t6");

    // Gegenstück: GeraetATests.T7_ConfigSync_AendertProfil.
    [Fact, TestPriority(70)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "Haupt")]
    public Task T7_ConfigSync_KommtOhneNeustartAn() =>
        Artefakte.SichereAsync(nameof(T7_ConfigSync_KommtOhneNeustartAn), async () =>
        {
            using var app = new HaelpMiApp();
            BeendeKonfiguration();
            var einstellungen = app.OeffneEinstellungen(TimeSpan.FromSeconds(30));
            var pid = AgentPid() ?? throw new InvalidOperationException($"{AgentProzess} läuft nicht.");
            var seit = DateTimeOffset.UtcNow;
            Rendezvous.Signal("t7-b-bereit");

            var zeilen = (await Rendezvous.WaitForAsync("t7-geaendert", GegenseiteTimeout)).Split('\n');
            var version = int.Parse(zeilen[0].Trim());
            var dauer = await WarteMitDauerAsync(
                () => LiesConfig()?.ConfigVersion >= version && LiesEinstellungen()?.AppliedConfigVersion >= version, SyncTimeout,
                () => $"ConfigVersion {version} ist {SyncTimeout.TotalSeconds:0} s nach der Marke nicht auf B angewendet " +
                $"(shared-config.json {LiesConfig()?.ConfigVersion}, AppliedConfigVersion {LiesEinstellungen()?.AppliedConfigVersion}).");

            var p2 = LiesConfig()!.AlarmProfiles.FirstOrDefault(p => p.Name == P2NameNeu);
            Assert.True(p2 is not null, $"Profil '{P2NameNeu}' fehlt nach dem Sync in shared-config.json.");
            Assert.Equal(HotkeyP2Neu, p2!.Hotkey);
            Assert.Equal(1, p2.ResponseThreshold);

            string? auditZeile = null;
            await WarteBisAsync(() => (auditZeile = AuditLogZeilen(seit).LastOrDefault(z => z.Contains($"configsync applied version={version}", StringComparison.Ordinal))) is not null,
                TimeSpan.FromSeconds(5), $"audit.log enthält keine Zeile 'configsync applied version={version}'.");
            await WarteBisAsync(() => NameVon(einstellungen, "MyAlarmHotkeyText") == HotkeyP2Neu.Format(), SyncTimeout,
                () => $"Einstellungen zeigen unter 'Meine Alarme' '{NameVon(einstellungen, "MyAlarmHotkeyText")}' statt '{HotkeyP2Neu.Format()}'.");

            var latenz = $"Config-Sync Version {version}: {dauer.TotalSeconds:0.0} s nach Eintreffen der Marke angewendet; " +
                $"Audit '{auditZeile}'; Änderung auf A um {(zeilen.Length > 1 ? zeilen[1].Trim() : "?")} (Uhren nicht abgeglichen)";
            _ausgabe.WriteLine(latenz);
            Directory.CreateDirectory(Artefakte.Ordner);
            File.AppendAllText(Path.Combine(Artefakte.Ordner, "t7-latenz-b.txt"), latenz + Environment.NewLine);
            BeendeKonfiguration();

            DrueckeHotkey(HotkeyP2);
            Rendezvous.Signal("t7-f10-aus");
            await WarteKeinFensterAsync(app.FindeSenderBanner, TimeSpan.FromSeconds(5), "Das alte Tastenkürzel löst auf B nach dem Config-Sync noch P2 aus.");
            await Rendezvous.WaitForAsync("t7-a-kein-popup", SchrittTimeout);

            DrueckeHotkey(HotkeyP2Neu);
            Rendezvous.Signal("t7-f12-aus");
            app.ErwarteBanner("P2 mit neuem Tastenkürzel");
            await Rendezvous.WaitForAsync("t7-a-quittiert", SchrittTimeout);
            await app.SchliesseSenderBannerAsync();

            Assert.True(AgentPid() == pid, $"Der Agent auf B wurde während des Config-Syncs neu gestartet (PID {pid} -> {AgentPid()}).");
        }, "t7");

    // Gegenstück: GeraetATests.T8_GeraetDeaktivierenUndAktivieren. Erwartung: deaktiviert = empfängt und sendet nicht.
    [Fact, TestPriority(80)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "Haupt")]
    public Task T8_Deaktiviert_WederEmpfangNochVersand() =>
        Artefakte.SichereAsync(nameof(T8_Deaktiviert_WederEmpfangNochVersand), async () =>
        {
            var befunde = new List<string>();
            using var app = new HaelpMiApp();
            Window? Toast() => app.FindeFenster(AgentProzess, null, "LicenseLimitToastWindow");

            await Rendezvous.WaitForAsync("t8-aus", GegenseiteTimeout);
            await WarteBisAsync(() => LiesEinstellungen()?.LicenseOverride == LicenseOverride.ForceDisabled, NegativDauer,
                () => $"settings.json auf B: LicenseOverride={LiesEinstellungen()?.LicenseOverride} statt ForceDisabled {NegativDauer.TotalSeconds:0} s nach dem Deaktivieren.");
            await PruefeBefundAsync(befunde, () => WarteBisAsync(() => Toast() is not null, NegativDauer,
                "Produktbefund: kein Hinweis-Toast (LicenseLimitToastWindow) auf dem deaktivierten Gerät B."));
            _ausgabe.WriteLine($"Toast auf B: '{(Toast() is { } toast ? NameVon(toast, "MessageText") : "-")}'");
            Rendezvous.Signal("t8-b-gesperrt");

            await PruefeBefundAsync(befunde, () => KeinFensterBisMarkeAsync(app.FindeAlarmfenster, "t8-a-gesendet", SchrittTimeout, TimeSpan.FromSeconds(10),
                "Produktbefund: das deaktivierte Gerät B zeigt ein Alarmfenster."));
            if (app.FindeAlarmfenster() is { } fremd)
            {
                await app.QuittiereUndSchliesseAsync(fremd, "B (deaktiviert)");
            }

            DrueckeHotkey(HotkeyP2Neu);
            Window? dialog = null;
            try
            {
                dialog = app.WarteAuf(() => app.FindeDialog(AgentProzess), "Hinweis-Dialog", TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                befunde.Add("Produktbefund: kein Hinweis-Dialog, nachdem das deaktivierte Gerät B das Tastenkürzel gedrückt hat.");
            }

            if (dialog is not null)
            {
                _ausgabe.WriteLine($"Hinweis-Dialog auf B: '{dialog.Title}'");
                var erwartet = $"HälpMi - {LicenseBlockTexts.Title(LicenseBlockReason.ManuallyDisabled)}";
                if (dialog.Title != erwartet)
                {
                    befunde.Add($"Produktbefund: Der Hinweis-Dialog auf B heißt '{dialog.Title}', erwartet '{erwartet}' (Grund: deaktiviert).");
                }

                var ok = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("OK")))
                    ?? dialog.FindFirstDescendant(cf => cf.ByAutomationId("2"))
                    ?? throw new InvalidOperationException("Knopf 'OK' im Hinweis-Dialog nicht gefunden.");
                ok.AsButton().Invoke();
            }

            Rendezvous.Signal("t8-b-f12-aus");
            await PruefeBefundAsync(befunde, () => WarteKeinFensterAsync(app.FindeSenderBanner, TimeSpan.FromSeconds(3),
                "Produktbefund: das deaktivierte Gerät B zeigt ein Sender-Banner."));
            await app.SchliesseSenderBannerAsync();

            await Rendezvous.WaitForAsync("t8-an", SchrittTimeout);
            await WarteBisAsync(() => LiesEinstellungen() is { } s && s.LicenseOverride != LicenseOverride.ForceDisabled, NegativDauer,
                $"settings.json auf B: nach dem Aktivieren weiterhin ForceDisabled.");
            await WarteBisAsync(() => Toast() is null, NegativDauer, "Der Hinweis-Toast bleibt nach dem Aktivieren auf B stehen.");
            Rendezvous.Signal("t8-b-frei");

            await Rendezvous.WaitForAsync("t8-a-f9-aus", SchrittTimeout);
            var popup = app.ErwartePopup("P1 von A nach dem Aktivieren");
            Element(popup, "OnMyWayButton").AsButton().Invoke();
            Rendezvous.Signal("t8-b-quittiert");
            var schliessen = Element(popup, "CloseButton").AsButton();
            await WarteBisAsync(() => schliessen.IsEnabled, TimeSpan.FromSeconds(15), "Schließen wird nach 'Bin unterwegs' nicht frei.");
            schliessen.Invoke();
            await WarteBisAsync(() => !app.AlarmfensterOffen(), TimeSpan.FromSeconds(10), "Alarmfenster ist nach dem Schließen noch offen.");

            DrueckeHotkey(HotkeyP2Neu);
            Rendezvous.Signal("t8-b-f12-an");
            app.ErwarteBanner("P2 von B nach dem Aktivieren");
            await Rendezvous.WaitForAsync("t8-a-fertig", SchrittTimeout);
            await app.SchliesseSenderBannerAsync();
            MeldeBefunde(befunde);
        }, "t8");

    private static bool ProfileAngekommen(Guid p1, Guid p2, Guid a, Guid b)
    {
        if (LiesConfig() is not { } config || LiesEinstellungen() is not { } settings || settings.AppliedConfigVersion < config.ConfigVersion)
        {
            return false;
        }

        var geraetA = new EntityRef(EntityKind.Device, a);
        var geraetB = new EntityRef(EntityKind.Device, b);
        var gruppe = config.DeviceGroups.FirstOrDefault(g => g.Name == GruppeName && g.DeviceIds.Contains(a));
        var profil1 = config.AlarmProfiles.FirstOrDefault(p => p.Id == p1);
        var profil2 = config.AlarmProfiles.FirstOrDefault(p => p.Id == p2);

        return gruppe is not null
            && profil1 is { ResponseThreshold: 1 } && profil1.Hotkey == HotkeyP1
            && profil1.RecipientAssignments.Any(z => z.Sender == geraetA && z.Recipients.Contains(geraetB))
            && profil2 is { ResponseThreshold: 2 } && profil2.Hotkey == HotkeyP2
            && profil2.RecipientAssignments.Any(z => z.Sender == geraetB && z.Recipients.Contains(new EntityRef(EntityKind.Group, gruppe.Id)));
    }
}
