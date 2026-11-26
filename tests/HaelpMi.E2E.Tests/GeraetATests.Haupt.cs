using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Runtime;
using HaelpMi.Core.Storage;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// Hauptfunktionen (#171), Gegenstücke in GeraetBTests.Haupt.cs.
public partial class GeraetATests
{
    private static readonly TimeSpan NegativDauer = TimeSpan.FromSeconds(10);

    [Fact, TestPriority(60)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "Haupt"), Trait("FR", "FR-50"), Trait("FR", "FR-51")]
    public Task T6_ZweiAlarmprofile_UeberDashboard() =>
        Artefakte.SichereAsync(nameof(T6_ZweiAlarmprofile_UeberDashboard), async () =>
        {
            var a = EigeneDeviceId();
            var b = await DeviceIdDerGegenseiteAsync("b");
            var raumB = RaumVon(await WarteAufGeraetAsync(b, DiscoveryTimeout));
            var geraetA = new EntityRef(EntityKind.Device, a);
            var geraetB = new EntityRef(EntityKind.Device, b);
            var befunde = new List<string>();

            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            var seite = DashboardSeite.Oeffne(app);

            seite.WaehleTab("Gruppen");
            var gruppe = await seite.NeueGruppeAsync();
            await seite.SetzeTextAsync("GroupNameBox", GruppeName,
                c => c.DeviceGroups.Any(g => g.Id == gruppe && g.Name == GruppeName), "GroupStatusText");
            await seite.KlickeListenEintragAsync("GroupDevicesList",
                n => n.Contains($" - {E2EUmgebung.Raum} (", StringComparison.Ordinal) && n.Contains("dieses Gerät", StringComparison.Ordinal),
                $"Gerät A ({E2EUmgebung.Raum})",
                () => LiesConfig()?.DeviceGroups.FirstOrDefault(g => g.Id == gruppe)?.DeviceIds.Contains(a) == true);
            await WarteBisAsync(() => LiesConfig()?.DeviceGroups.FirstOrDefault(g => g.Id == gruppe)?.DeviceIds.Contains(a) == true,
                TimeSpan.FromSeconds(15), $"Gerät A ist nach dem Klick nicht Mitglied von '{GruppeName}' in shared-config.json.");

            // Gleicher Windows-Benutzer auf A und B: die Einträge unterscheiden sich nur im Raum.
            Func<string, bool> eintragA = n => n.Contains($"({E2EUmgebung.Raum}, dieses Gerät)", StringComparison.Ordinal);
            Func<string, bool> eintragB = n => n.Contains($"({raumB})", StringComparison.Ordinal);

            seite.WaehleTab("Alarm-Profile");
            var p1 = await seite.NeuesProfilAsync();
            await RichteProfilEinAsync(seite, p1, P1Name, 1, HotkeyP1);
            await seite.KlickeListenEintragAsync("SenderUsersList", eintragA, "Sender A", () => seite.HatListenEintraege("RecipientUsersList"));
            await seite.KlickeListenEintragAsync("RecipientUsersList", eintragB, "Empfänger B", () => HatZuordnung(p1, geraetA, geraetB));
            await WarteAufZuordnungAsync(seite, p1, geraetA, geraetB);

            var p2 = await seite.NeuesProfilAsync();
            await RichteProfilEinAsync(seite, p2, P2Name, 2, HotkeyP2);
            var gruppeRef = new EntityRef(EntityKind.Group, gruppe);
            // Die Empfängerliste von Sender A ist noch gefüllt, erkennbar ist der Wechsel an der Markierung von B.
            await seite.KlickeListenEintragAsync("SenderUsersList", eintragB, "Sender B");
            await Task.Delay(500);
            await seite.KlickeListenEintragAsync("RecipientGroupsList", n => n.Contains($"Gruppe: {GruppeName}", StringComparison.Ordinal), $"Gruppe {GruppeName}",
                () => HatZuordnung(p2, geraetB, gruppeRef));
            await WarteAufZuordnungAsync(seite, p2, geraetB, gruppeRef);

            BeendeKonfiguration();
            Rendezvous.Signal("t6-profile-da", $"{p1}\n{p2}");
            await Rendezvous.WaitForAsync("t6-b-bereit", SchrittTimeout);

            // P1: A an B, Schwelle 1.
            DrueckeHotkey(HotkeyP1);
            Rendezvous.Signal("t6-p1-aus");
            var banner = app.ErwarteBanner("P1 (A an B)");
            await Rendezvous.WaitForAsync("t6-b-p1-quittiert", SchrittTimeout);
            await PruefeBefundAsync(befunde, () => WarteBisAsync(() => NameVon(banner, "AckedText") == "Empfangen: 1 von 1", TimeSpan.FromSeconds(15),
                () => "P1: " + AckBefund(NameVon(banner, "AckedText"))));
            await app.SchliesseSenderBannerAsync();

            // P2 hat nur B als Sender: auf A darf F10 nichts auslösen.
            DrueckeHotkey(HotkeyP2);
            Rendezvous.Signal("t6-p2a-aus");
            await WarteKeinFensterAsync(app.FindeSenderBanner, NegativDauer, "P2 auf A ausgelöst: A ist dort kein Sender, trotzdem erscheint ein Banner.");

            // P2 von B: A ist über die Gruppe Empfänger, Schwelle 2 mit nur einem Empfänger.
            await Rendezvous.WaitForAsync("t6-p2b-aus", SchrittTimeout);
            var popup = app.ErwartePopup("P2 von B (A über Gruppe Empfänger)");
            Assert.Contains("noch 2", NameVon(popup, "ThresholdStatusText"));
            Element(popup, "OnMyWayButton").AsButton().Invoke();
            var schliessen = Element(popup, "CloseButton").AsButton();
            var bis = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < bis)
            {
                Assert.False(schliessen.IsEnabled, $"P2: Schließen ist nach einer von zwei Rückmeldungen frei ({NameVon(popup, "ThresholdStatusText")}).");
                await Task.Delay(300);
            }

            Rendezvous.Signal("t6-a-unterwegs");
            await Rendezvous.WaitForAsync("t6-b-abgebrochen", SchrittTimeout);
            await WarteBisAsync(() => schliessen.IsEnabled, TimeSpan.FromSeconds(15),
                () => $"P2: Schließen wird nach dem Abbrechen auf B nicht frei ({NameVon(popup, "ThresholdStatusText")}).");
            schliessen.Invoke();
            await WarteBisAsync(() => !app.AlarmfensterOffen(), TimeSpan.FromSeconds(10), "P2: Alarmfenster ist nach dem Schließen noch offen.");
            Rendezvous.Signal("t6-a-p2-fertig");

            // P1 hat nur A als Sender.
            await Rendezvous.WaitForAsync("t6-b-p1-aus", SchrittTimeout);
            await WarteKeinFensterAsync(app.FindeAlarmfenster, NegativDauer, "P1 auf B ausgelöst: B ist dort kein Sender, trotzdem erscheint auf A ein Alarmfenster.");
            MeldeBefunde(befunde);
        }, "t6");

    // Gegenstück: GeraetBTests.T7_ConfigSync_KommtOhneNeustartAn.
    [Fact, TestPriority(70)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "Haupt")]
    public Task T7_ConfigSync_AendertProfil() =>
        Artefakte.SichereAsync(nameof(T7_ConfigSync_AendertProfil), async () =>
        {
            await Rendezvous.WaitForAsync("t7-b-bereit", GegenseiteTimeout);

            using var app = new HaelpMiApp();
            var seite = DashboardSeite.Oeffne(app);
            await seite.WaehleProfilAsync(P2Name);
            var p2 = LiesConfig()?.AlarmProfiles.FirstOrDefault(p => p.Name == P2Name)?.Id
                ?? throw new InvalidOperationException($"Profil {P2Name} fehlt in shared-config.json, T6 lief nicht.");

            await seite.SetzeTextAsync("ProfileNameBox", P2NameNeu, c => Profil(c, p2)?.Name == P2NameNeu);
            await seite.SetzeTextAsync("ProfileThresholdBox", "1", c => Profil(c, p2)?.ResponseThreshold == 1);
            await seite.SetzeHotkeyAsync(HotkeyP2Neu, c => Profil(c, p2)?.Hotkey == HotkeyP2Neu);
            var version = LiesConfig()!.ConfigVersion;
            BeendeKonfiguration();
            Rendezvous.Signal("t7-geaendert", $"{version}\n{DateTimeOffset.UtcNow:O}");

            await Rendezvous.WaitForAsync("t7-f10-aus", SchrittTimeout);
            await WarteKeinFensterAsync(app.FindeAlarmfenster, NegativDauer, "B löst mit dem alten Tastenkürzel noch P2 aus: die Änderung ist auf B nicht wirksam.");
            Rendezvous.Signal("t7-a-kein-popup");

            await Rendezvous.WaitForAsync("t7-f12-aus", SchrittTimeout);
            await app.QuittiereUndSchliesseAsync(app.ErwartePopup("P2 mit neuem Tastenkürzel von B"), "A");
            Rendezvous.Signal("t7-a-quittiert");
        }, "t7");

    // Gegenstück: GeraetBTests.T8_Deaktiviert_WederEmpfangNochVersand.
    [Fact, TestPriority(80)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "Haupt")]
    public Task T8_GeraetDeaktivierenUndAktivieren() =>
        Artefakte.SichereAsync(nameof(T8_GeraetDeaktivierenUndAktivieren), async () =>
        {
            var b = await DeviceIdDerGegenseiteAsync("b");
            var config = LiesConfig();
            var p1 = config?.AlarmProfiles.FirstOrDefault(p => p.Name == P1Name);
            Assert.True(p1 is not null && HatZuordnung(p1.Id, new EntityRef(EntityKind.Device, EigeneDeviceId()), new EntityRef(EntityKind.Device, b))
                && config!.AlarmProfiles.Any(p => p.Name == P2NameNeu && p.Hotkey == HotkeyP2Neu),
                $"Vorbedingung fehlt: {P1Name} (A an B) aus T6 und {P2NameNeu} mit neuem Tastenkürzel aus T7 sind nicht in shared-config.json.");
            var befunde = new List<string>();
            using var app = new HaelpMiApp();
            var seite = DashboardSeite.Oeffne(app);
            seite.WaehleTabPerId("DevicesTabItem");

            var schalter = await GeraeteSchalterAsync(seite, b);
            Assert.True(schalter.AsToggleButton().ToggleState == ToggleState.On, "B ist vor dem Test nicht aktiv.");
            // Ein Umschalten per TogglePattern ändert nur die Anzeige; gespeichert wird im Click-Handler.
            schalter.Click();
            await WarteBisAsync(() => UeberschreibungVon(b) == LicenseOverride.ForceDisabled, TimeSpan.FromSeconds(10),
                () => $"devices.json: B hat nach dem Deaktivieren LicenseOverride={UeberschreibungVon(b)} statt ForceDisabled.");
            Rendezvous.Signal("t8-aus");

            await Rendezvous.WaitForAsync("t8-b-gesperrt", SchrittTimeout);
            // Der einzige Empfänger ist deaktiviert, also 0 erreichbare Empfänger: das Popup aus Entwurf B2
            // (Owner-Entscheidung 29), eine Welle, keine Wiederholung, Ausblenden nach einigen Sekunden.
            DrueckeHotkey(HotkeyP1);
            var gesperrtBanner = app.ErwarteBanner("P1 mit deaktiviertem Empfänger B");
            await PruefeBefundAsync(befunde, () => WarteBisAsync(() => NameVon(gesperrtBanner, "StatusText") == "Kein Empfänger eingestellt!", TimeSpan.FromSeconds(5),
                () => $"Produktbefund: Das Popup meldet bei deaktiviertem Empfänger B '{NameVon(gesperrtBanner, "StatusText")}' statt \"Kein Empfänger eingestellt!\"."));
            if (SichtbarerText(gesperrtBanner, "CancelButton") != string.Empty || SichtbarerText(gesperrtBanner, "AckedText") != string.Empty)
            {
                befunde.Add($"Produktbefund: Das Popup zeigt bei deaktiviertem Empfänger B noch Zähler '{SichtbarerText(gesperrtBanner, "AckedText")}' " +
                    $"bzw. Abbrechen-Knopf '{SichtbarerText(gesperrtBanner, "CancelButton")}'.");
            }
            await PruefeBefundAsync(befunde, () => WarteBisAsync(() => app.FindeSenderBanner() is null, TimeSpan.FromSeconds(15),
                "Produktbefund: Das Popup bei deaktiviertem Empfänger B blendet sich nicht von selbst aus."));
            await app.SchliesseSenderBannerAsync();

            // Am Empfängerfilter von A vorbei: belegt die Empfangssperre von B für sich allein.
            var ergebnis = await SendeDirektAnAsync(b);
            Rendezvous.Signal("t8-a-gesendet");
            _ausgabe.WriteLine($"Direktversand an das deaktivierte B: {ergebnis.AckedCount} von {ergebnis.TargetCount} bestätigt.");
            if (ergebnis.AckedCount != 0)
            {
                befunde.Add($"Produktbefund: das deaktivierte Gerät B hat einen direkt gesendeten Alarm bestätigt ({ergebnis.AckedCount} von {ergebnis.TargetCount}).");
            }

            await Rendezvous.WaitForAsync("t8-b-f12-aus", SchrittTimeout);
            await PruefeBefundAsync(befunde, () => WarteKeinFensterAsync(app.FindeAlarmfenster, NegativDauer,
                "Produktbefund: das deaktivierte Gerät B kann weiter Alarme senden, A zeigt ein Alarmfenster."));
            if (app.FindeAlarmfenster() is { } fremd)
            {
                await app.QuittiereUndSchliesseAsync(fremd, "A (Alarm vom deaktivierten B)");
            }

            seite.Fenster.SetForeground();
            (await GeraeteSchalterAsync(seite, b)).Click();
            await WarteBisAsync(() => UeberschreibungVon(b) is { } wert && wert != LicenseOverride.ForceDisabled, TimeSpan.FromSeconds(10),
                () => $"devices.json: B hat nach dem Aktivieren LicenseOverride={UeberschreibungVon(b)}.");
            _ausgabe.WriteLine($"LicenseOverride von B nach dem Aktivieren: {UeberschreibungVon(b)}");
            if (UeberschreibungVon(b) != LicenseOverride.None)
            {
                befunde.Add($"Produktbefund: Aktivieren setzt LicenseOverride={UeberschreibungVon(b)} statt None (#180).");
            }
            BeendeKonfiguration();
            Rendezvous.Signal("t8-an");

            await Rendezvous.WaitForAsync("t8-b-frei", SchrittTimeout);
            DrueckeHotkey(HotkeyP1);
            Rendezvous.Signal("t8-a-f9-aus");
            var banner = app.ErwarteBanner("P1 nach dem Aktivieren");
            await Rendezvous.WaitForAsync("t8-b-quittiert", SchrittTimeout);
            await PruefeBefundAsync(befunde, () => WarteBisAsync(() => NameVon(banner, "AckedText") == "Empfangen: 1 von 1", TimeSpan.FromSeconds(15),
                () => "Nach dem Aktivieren: " + AckBefund(NameVon(banner, "AckedText"))));
            await app.SchliesseSenderBannerAsync();

            await Rendezvous.WaitForAsync("t8-b-f12-an", SchrittTimeout);
            await app.QuittiereUndSchliesseAsync(app.ErwartePopup("P2 von B nach dem Aktivieren"), "A");
            Rendezvous.Signal("t8-a-fertig");
            MeldeBefunde(befunde);
        }, "t8");

    private static async Task RichteProfilEinAsync(DashboardSeite seite, Guid id, string name, int schwelle, HotkeyDefinition hotkey)
    {
        await seite.SetzeTextAsync("ProfileNameBox", name, c => Profil(c, id)?.Name == name);
        await seite.SetzeTextAsync("ProfileThresholdBox", schwelle.ToString(), c => Profil(c, id)?.ResponseThreshold == schwelle);
        await seite.SetzeHotkeyAsync(hotkey, c => Profil(c, id)?.Hotkey == hotkey);
    }

    private static bool HatZuordnung(Guid profil, EntityRef sender, EntityRef empfaenger) =>
        LiesConfig() is { } c && Profil(c, profil)?.RecipientAssignments.Any(z => z.Sender == sender && z.Recipients.Contains(empfaenger)) == true;

    private static Task WarteAufZuordnungAsync(DashboardSeite seite, Guid profil, EntityRef sender, EntityRef empfaenger) =>
        WarteBisAsync(() => HatZuordnung(profil, sender, empfaenger), TimeSpan.FromSeconds(15),
            () => $"Zuordnung {sender.Kind} {sender.Id} an {empfaenger.Kind} {empfaenger.Id} ist nicht in shared-config.json angekommen " +
            $"(Statustext '{seite.Statustext()}', Empfängerliste: {string.Join(" | ", seite.ListenNamen("RecipientUsersList"))}).");

    private static AlarmProfile? Profil(SharedConfig config, Guid id) => config.AlarmProfiles.FirstOrDefault(p => p.Id == id);

    private static string RaumVon(DeviceEntry geraet) => string.IsNullOrWhiteSpace(geraet.RoomName) ? "kein Raum" : geraet.RoomName;

    private static LicenseOverride? UeberschreibungVon(Guid deviceId) =>
        LiesGeraete().FirstOrDefault(d => d.DeviceId == deviceId)?.LicenseOverride;

    private static async Task<AutomationElement> GeraeteSchalterAsync(DashboardSeite seite, Guid deviceId)
    {
        var id = $"DeviceActiveToggle_{deviceId}";
        AutomationElement? schalter = null;
        await WarteBisAsync(() => (schalter = seite.Fenster.FindFirstDescendant(cf => cf.ByAutomationId(id))) is not null,
            TimeSpan.FromSeconds(15), $"Schalter {id} fehlt im Reiter 'Geräte / Lizenz'.");
        schalter!.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        await WarteBisAsync(() => schalter.IsEnabled, TimeSpan.FromSeconds(5),
            () => $"Schalter {id} ist gesperrt ({schalter.Properties.HelpText.ValueOrDefault}).");
        return schalter;
    }

    private static async Task<AlarmSendResult> SendeDirektAnAsync(Guid empfaenger)
    {
        var deployment = JsonFileStore.Load<DeploymentInfo>(Path.Combine(E2EUmgebung.ProgrammOrdner, "deployment.json"))
            ?? throw new InvalidOperationException("deployment.json fehlt.");
        var settings = LiesEinstellungen() ?? throw new InvalidOperationException("settings.json nicht lesbar.");
        var profil = LiesConfig()?.AlarmProfiles.FirstOrDefault(p => p.Name == P1Name)
            ?? throw new InvalidOperationException($"Profil {P1Name} fehlt in shared-config.json.");
        var ziel = LiesGeraete().FirstOrDefault(d => d.DeviceId == empfaenger)
            ?? throw new InvalidOperationException($"Gerät {empfaenger} fehlt in devices.json.");

        return await new AlarmSender().SendAsync(profil, Guid.NewGuid(), LiveIdentityFactory.Create(settings, deployment), new[] { ziel });
    }
}
