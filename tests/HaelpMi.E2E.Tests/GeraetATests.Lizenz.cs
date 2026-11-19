using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T9 Lizenz (#171), Gegenstück in GeraetBTests.Lizenz.cs.
public partial class GeraetATests
{
    private const string ErinnerungsToast = "LicenseReminderToastWindow";

    // Stufen: L1 ohne Lizenz, L2 Import im Dashboard ohne Neustart, L3 bald ablaufend, L4 abgelaufen, L5 gültig.
    [Fact, TestPriority(90)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "Haupt")]
    public Task T9_Lizenz_FehltEinspielenBaldAbgelaufen() =>
        Artefakte.SichereAsync(nameof(T9_Lizenz_FehltEinspielenBaldAbgelaufen), async () =>
        {
            var b = await DeviceIdDerGegenseiteAsync("b");
            var config = LiesConfig();
            Assert.True(config?.AlarmProfiles.Any(p => p.Name == P1Name) == true && config.AlarmProfiles.Any(p => p.Name == P2NameNeu),
                $"Vorbedingung fehlt: {P1Name} (A an B) und {P2NameNeu} (B an A) aus T6/T7 sind nicht in shared-config.json.");
            var befunde = new List<string>();
            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();

            // L1: ohne Lizenz. Admin-Geräte bleiben immer aktiv (LicenseLimitGuard), B hat Kontingent 0.
            await Lizenz.WechselnAsync("l1", "a", () =>
            {
                Lizenz.Entfernen();
                Lizenz.UeberschreibungZuruecksetzen(b);
            }, GegenseiteTimeout);
            await ErwarteErinnerungAsync(app, befunde, "L1", t => t.StartsWith("Keine Lizenz", StringComparison.Ordinal), "'Keine Lizenz gefunden …'");
            var (banner, bearbeitbar) = await DashboardLizenzstandAsync(app, "L1");
            if (!banner.Contains("Keine Lizenz", StringComparison.Ordinal))
            {
                befunde.Add($"Produktbefund L1: Lizenz-Banner im Dashboard zeigt '{banner}' statt 'Keine Lizenz …'.");
            }

            if (bearbeitbar)
            {
                befunde.Add("Produktbefund L1: ohne Lizenz ist 'Neues Alarm-Profil' im Dashboard nicht gesperrt.");
            }

            await PruefeBefundAsync(befunde, () => SelbsttestAsync(app, "L1 (Admin ohne Lizenz)"));
            await app.SchliesseSenderBannerAsync();

            await Rendezvous.WaitForAsync("t9-l1-b-bereit", SchrittTimeout);
            var direkt = await SendeDirektAnAsync(b);
            _ausgabe.WriteLine($"L1: Direktversand an B ohne Lizenz: {direkt.AckedCount} von {direkt.TargetCount} bestätigt.");
            if (direkt.AckedCount != 0)
            {
                befunde.Add($"Produktbefund L1: B ohne Lizenz hat einen direkt gesendeten Alarm bestätigt ({direkt.AckedCount} von {direkt.TargetCount}).");
            }

            Rendezvous.Signal("t9-l1-a-gesendet");
            await PruefeBefundAsync(befunde, () => KeinFensterBisMarkeAsync(app.FindeAlarmfenster, "t9-l1-b-f12-aus", SchrittTimeout, NegativDauer,
                "Produktbefund L1: B ohne Lizenz kann Alarme senden, A zeigt ein Alarmfenster."));
            if (app.FindeAlarmfenster() is { } fremd)
            {
                await app.QuittiereUndSchliesseAsync(fremd, "A (Alarm vom lizenzlosen B)");
            }

            // L2: gültige Lizenz im Dashboard einspielen, ohne Neustart; B übernimmt per Gossip.
            var pid = AgentPid();
            await EinspielenImDashboardAsync(app, befunde);
            var uebernahme = (await Rendezvous.WaitForAsync("t9-l2-b-lizenz", SchrittTimeout)).Split('\n');
            if (uebernahme[0].Trim() == "ok")
            {
                await AlarmAnBAsync(app, "l2");
            }
            else
            {
                _ausgabe.WriteLine("L2: B hat die Lizenz nicht übernommen, Alarm an B entfällt (Befund auf B).");
            }

            if (AgentPid() != pid)
            {
                befunde.Add($"L2: der Agent auf A wurde während des Einspielens neu gestartet (PID {pid} -> {AgentPid()}).");
            }

            // L3: läuft in 10 Tagen ab, Warnung ohne Sperre; "Später erinnern" hält über einen Neustart.
            await Lizenz.WechselnAsync("l3", "a", () => Lizenz.Einspielen(Lizenz.Bald), SchrittTimeout);
            await ErwarteErinnerungAsync(app, befunde, "L3", t => Regex.IsMatch(t, @"^Lizenz läuft in (9|10) Tag\(en\) ab"), "'Lizenz läuft in 9/10 Tag(en) ab'");
            (banner, bearbeitbar) = await DashboardLizenzstandAsync(app, "L3");
            if (!bearbeitbar)
            {
                befunde.Add($"Produktbefund L3: eine noch gültige, bald ablaufende Lizenz sperrt 'Neues Alarm-Profil' (Banner '{banner}').");
            }

            await Rendezvous.WaitForAsync("t9-l3-b-bereit", SchrittTimeout);
            await AlarmAnBAsync(app, "l3");

            if (app.FindeToast(ErinnerungsToast) is { } toast)
            {
                Element(toast, "SnoozeButton").AsButton().Invoke();
                await PruefeBefundAsync(befunde, () => WarteBisAsync(() => app.FindeToast(ErinnerungsToast) is null, TimeSpan.FromSeconds(5),
                    "Produktbefund L3: der Erinnerungs-Toast schließt sich nach 'Später erinnern' nicht."));
                if (!File.Exists(AppPaths.LicenseReminderStateFilePath))
                {
                    befunde.Add($"Produktbefund L3: 'Später erinnern' schreibt {AppPaths.LicenseReminderStateFilePath} nicht.");
                }

                await AgentNeustartMitAnlaufAsync();
                await PruefeBefundAsync(befunde, () => WarteKeinFensterAsync(() => app.FindeToast(ErinnerungsToast), NegativDauer,
                    "Produktbefund L3: der Erinnerungs-Toast erscheint nach 'Später erinnern' beim nächsten Start wieder."));
            }

            // L4: abgelaufen. Soft-Expiry: Alarme in beide Richtungen, nur die Bearbeitung ist gesperrt.
            await Lizenz.WechselnAsync("l4", "a", () => Lizenz.Einspielen(Lizenz.Abgelaufen), SchrittTimeout);
            await ErwarteErinnerungAsync(app, befunde, "L4",
                t => Regex.IsMatch(t, @"^Lizenz seit \d+ Tag\(en\) abgelaufen") && t.Contains("Bearbeitung ist bis dahin gesperrt", StringComparison.Ordinal),
                "'Lizenz seit … abgelaufen … Bearbeitung ist bis dahin gesperrt …'");
            (banner, bearbeitbar) = await DashboardLizenzstandAsync(app, "L4");
            if (bearbeitbar)
            {
                befunde.Add($"Produktbefund L4: mit abgelaufener Lizenz ist 'Neues Alarm-Profil' nicht gesperrt (Banner '{banner}').");
            }

            await Rendezvous.WaitForAsync("t9-l4-b-bereit", SchrittTimeout);
            await AlarmAnBAsync(app, "l4");
            await Rendezvous.WaitForAsync("t9-l4-p2-aus", SchrittTimeout);
            await app.QuittiereUndSchliesseAsync(app.ErwartePopup("L4: P2 von B bei abgelaufener Lizenz (Soft-Expiry)"), "A");
            Rendezvous.Signal("t9-l4-a-quittiert");

            // L5: gültige Lizenz zurück, Ausgangszustand für T10.
            await Lizenz.WechselnAsync("l5", "a", () => Lizenz.Einspielen(Lizenz.Gueltig), SchrittTimeout);
            Assert.Equal(LicenseStatus.Valid, Lizenz.Status().Status);
            await PruefeBefundAsync(befunde, () => WarteKeinFensterAsync(() => app.FindeToast(ErinnerungsToast), NegativDauer,
                "Produktbefund L5: Erinnerungs-Toast trotz gültiger Lizenz."));
            MeldeBefunde(befunde);
        }, "t9");

    private async Task ErwarteErinnerungAsync(HaelpMiApp app, List<string> befunde, string stufe, Func<string, bool> passt, string erwartet)
    {
        Window? toast = null;
        var text = string.Empty;
        await PruefeBefundAsync(befunde, () => WarteBisAsync(
            () => (toast = app.FindeToast(ErinnerungsToast)) is not null && (text = NameVon(toast, "MessageText")).Length > 0,
            TimeSpan.FromSeconds(20), $"Produktbefund {stufe}: kein Erinnerungs-Toast ({ErinnerungsToast}) auf A nach dem Agent-Start."));
        if (toast is null)
        {
            return;
        }

        _ausgabe.WriteLine($"{stufe}: Erinnerungs-Toast '{text}'");
        if (!passt(text))
        {
            befunde.Add($"Produktbefund {stufe}: Erinnerungs-Toast zeigt '{text}', erwartet {erwartet}.");
        }
    }

    /// <returns>Sichtbarer Text des Lizenz-Banners und ob "Neues Alarm-Profil" bedienbar ist.</returns>
    private async Task<(string Banner, bool Bearbeitbar)> DashboardLizenzstandAsync(HaelpMiApp app, string stufe)
    {
        var seite = DashboardSeite.Oeffne(app);
        seite.WaehleTab("Alarm-Profile");
        await Task.Delay(1000);
        var banner = SichtbarerText(seite.Fenster, "LicenseWarningText");
        var bearbeitbar = Element(seite.Fenster, "NewProfileButton").IsEnabled;
        _ausgabe.WriteLine($"{stufe}: Dashboard-Banner '{banner}', 'Neues Alarm-Profil' {(bearbeitbar ? "frei" : "gesperrt")}");
        BeendeKonfiguration();
        return (banner, bearbeitbar);
    }

    private async Task SelbsttestAsync(HaelpMiApp app, string fall)
    {
        BeendeKonfiguration();
        var einstellungen = app.OeffneEinstellungen(FensterTimeout);
        Element(einstellungen, "SelfTestButton").AsButton().Invoke();
        await app.QuittiereUndSchliesseAsync(app.ErwartePopup($"Selbsttest {fall}"), "A (Selbsttest)");
        BeendeKonfiguration();
    }

    /// <summary>Wie ein Admin: Banner-Knopf "Lizenz einspielen", Schlüsseltext einfügen, bestätigen.</summary>
    private async Task EinspielenImDashboardAsync(HaelpMiApp app, List<string> befunde)
    {
        var seite = DashboardSeite.Oeffne(app);
        seite.WaehleTab("Alarm-Profile");
        Element(seite.Fenster, "ImportLicenseButton").AsButton().Invoke();

        AutomationElement? feld = null;
        await WarteBisAsync(() => (feld = app.FindeElement(KonfigProzess, "KeyTextBox")) is not null, TimeSpan.FromSeconds(10),
            "Der Dialog 'Lizenz einspielen' öffnet sich nicht.");
        feld!.AsTextBox().Text = Lizenz.Schluesseltext(Lizenz.Gueltig);
        (app.FindeElement(KonfigProzess, "ImportButton") ?? throw new InvalidOperationException("Knopf 'Einspielen' fehlt.")).AsButton().Invoke();

        AutomationElement? meldung = null;
        await WarteBisAsync(() => (meldung = app.FindeDialogUeberall(KonfigProzess)) is not null, TimeSpan.FromSeconds(10),
            () => $"Keine Bestätigung nach 'Einspielen' (Fehlertext im Dialog: '{(app.FindeElement(KonfigProzess, "ErrorText") is { } f ? f.Name : "-")}').");
        var eingespielt = DateTimeOffset.UtcNow;
        Rendezvous.Signal("t9-l2-importiert", $"{eingespielt:O}");
        _ausgabe.WriteLine($"L2: Bestätigung '{meldung!.Name}'");
        BestaetigeDialog(meldung);

        await PruefeBefundAsync(befunde, () => WarteBisAsync(
            () => SichtbarerText(seite.Fenster, "LicenseWarningText").Length == 0 && Element(seite.Fenster, "NewProfileButton").IsEnabled,
            TimeSpan.FromSeconds(10),
            () => $"Produktbefund L2: nach dem Einspielen zeigt das Dashboard weiter '{SichtbarerText(seite.Fenster, "LicenseWarningText")}' " +
                $"bzw. 'Neues Alarm-Profil' bleibt gesperrt."));
        Assert.Equal(LicenseStatus.Valid, Lizenz.Status().Status);

        // Erst nach Banner und Geräteliste meldet das Dashboard dem Agent die neue Lizenz (IPC LicenseRenewed, danach
        // Announce an B). Ein früheres Beenden des Konfigurationsprogramms verschluckte diese Meldung.
        await PruefeBefundAsync(befunde, () => WarteBisAsync(() => app.FindeToast(ErinnerungsToast) is null, TimeSpan.FromSeconds(15),
            "Produktbefund L2: der Erinnerungs-Toast bleibt nach dem Einspielen stehen."));
        BeendeKonfiguration();
    }

    private async Task AlarmAnBAsync(HaelpMiApp app, string stufe)
    {
        DrueckeHotkey(HotkeyP1);
        Rendezvous.Signal($"t9-{stufe}-p1-aus");
        var banner = app.ErwarteBanner($"{stufe.ToUpperInvariant()}: P1 (A an B)");
        await Rendezvous.WaitForAsync($"t9-{stufe}-b-quittiert", SchrittTimeout);
        await app.SchliesseBannerAsync(banner);
    }
}
