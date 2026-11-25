using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using HaelpMi.Core.Licensing;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T9 Lizenz (#171), Gegenstück in GeraetATests.Lizenz.cs.
public partial class GeraetBTests
{
    private const string LimitToast = "LicenseLimitToastWindow";
    private const string ErinnerungsToast = "LicenseReminderToastWindow";

    [Fact, TestPriority(90)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "Haupt")]
    public Task T9_Lizenz_UserGeraetFolgtDerLizenz() =>
        Artefakte.SichereAsync(nameof(T9_Lizenz_UserGeraetFolgtDerLizenz), async () =>
        {
            var befunde = new List<string>();
            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();

            // L1: ohne Lizenz.
            await Lizenz.WechselnAsync("l1", "b", Lizenz.Entfernen, GegenseiteTimeout);
            await PruefeBefundAsync(befunde, () => WarteBisAsync(() => app.FindeToast(LimitToast) is not null, NegativDauer,
                $"Produktbefund L1: kein Hinweis-Toast ({LimitToast}) auf B ohne Lizenz."));
            _ausgabe.WriteLine($"L1: Toast auf B '{(app.FindeToast(LimitToast) is { } toast ? NameVon(toast, "MessageText") : "-")}'");
            Rendezvous.Signal("t9-l1-b-bereit");

            await PruefeBefundAsync(befunde, () => KeinFensterBisMarkeAsync(app.FindeAlarmfenster, "t9-l1-a-gesendet", SchrittTimeout, TimeSpan.FromSeconds(5),
                "Produktbefund L1: B ohne Lizenz zeigt einen direkt gesendeten Alarm an."));
            if (app.FindeAlarmfenster() is { } fremd)
            {
                await app.QuittiereUndSchliesseAsync(fremd, "B (ohne Lizenz)");
            }

            DrueckeHotkey(HotkeyP2Neu);
            AutomationElement? dialog = null;
            try
            {
                dialog = app.WarteAuf(() => app.FindeDialog(AgentProzess), "Hinweis-Dialog", TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                befunde.Add("Produktbefund L1: kein Hinweis-Dialog, nachdem B ohne Lizenz das Tastenkürzel gedrückt hat.");
            }

            if (dialog is not null)
            {
                _ausgabe.WriteLine($"L1: Hinweis-Dialog auf B '{dialog.Name}'");
                var erwartet = $"HälpMi - {LicenseBlockTexts.Title(LicenseBlockReason.NoLicense)}";
                if (dialog.Name != erwartet)
                {
                    befunde.Add($"Produktbefund L1: Der Hinweis-Dialog auf B heißt '{dialog.Name}', erwartet '{erwartet}' (Grund: keine Lizenz).");
                }

                BestaetigeDialog(dialog);
            }

            Rendezvous.Signal("t9-l1-b-f12-aus");
            await PruefeBefundAsync(befunde, () => WarteKeinFensterAsync(app.FindeSenderBanner, TimeSpan.FromSeconds(3),
                "Produktbefund L1: B ohne Lizenz zeigt ein Sender-Banner."));
            await app.SchliesseSenderBannerAsync();

            // L2: A spielt die Lizenz im Dashboard ein, B übernimmt sie per Gossip ohne Neustart.
            var pid = AgentPid();
            // Die Übernahme kann schon vor der Marke geschehen sein, die Audit-Zeile wird deshalb ab hier gesucht.
            var seit = DateTimeOffset.UtcNow;
            var eingespielt = (await Rendezvous.WaitForAsync("t9-l2-importiert", GegenseiteTimeout)).Trim();
            var uebernommen = false;
            try
            {
                var dauer = await WarteMitDauerAsync(() => Lizenz.Status().Status == LicenseStatus.Valid && app.FindeToast(LimitToast) is null,
                    TimeSpan.FromSeconds(60), () => $"Produktbefund L2: B übernimmt die in A eingespielte Lizenz nicht binnen 60 s " +
                        $"(Lizenzstatus {Lizenz.Status().Status}, Toast {(app.FindeToast(LimitToast) is null ? "weg" : "noch da")}).");
                var audit = AuditLogZeilen(seit).FirstOrDefault(z => z.Contains("Lizenz von Peer", StringComparison.Ordinal));
                var latenz = $"Lizenz-Übernahme auf B {dauer.TotalSeconds:0.0} s nach Eintreffen der Marke; eingespielt auf A um {eingespielt} " +
                    $"(Uhren nicht abgeglichen); Audit '{audit}'";
                _ausgabe.WriteLine(latenz);
                Directory.CreateDirectory(Artefakte.Ordner);
                File.AppendAllText(Path.Combine(Artefakte.Ordner, "t9-latenz-b.txt"), latenz + Environment.NewLine);
                Rendezvous.Signal("t9-l2-b-lizenz", $"ok\n{dauer.TotalSeconds:0.0}");
                uebernommen = true;
            }
            catch (Xunit.Sdk.XunitException ex)
            {
                befunde.Add(ex.Message);
                Rendezvous.Signal("t9-l2-b-lizenz", "fehlt");
            }

            if (uebernommen)
            {
                await AlarmVonAAsync(app, "l2");
            }

            if (AgentPid() != pid)
            {
                befunde.Add($"L2: der Agent auf B wurde während der Lizenzübernahme neu gestartet (PID {pid} -> {AgentPid()}).");
            }

            // L3: bald ablaufend - B bleibt aktiv.
            await Lizenz.WechselnAsync("l3", "b", () => Lizenz.Einspielen(Lizenz.Bald), SchrittTimeout);
            if (app.FindeToast(LimitToast) is not null)
            {
                befunde.Add("Produktbefund L3: B zeigt mit bald ablaufender, gültiger Lizenz den Sperr-Toast.");
            }

            Rendezvous.Signal("t9-l3-b-bereit");
            await AlarmVonAAsync(app, "l3");

            // L4: abgelaufen - Soft-Expiry, B sendet und empfängt weiter.
            await Lizenz.WechselnAsync("l4", "b", () => Lizenz.Einspielen(Lizenz.Abgelaufen), SchrittTimeout);
            if (app.FindeToast(LimitToast) is not null)
            {
                befunde.Add("Produktbefund L4: B ist mit abgelaufener Lizenz gesperrt (Sperr-Toast), erwartet ist Soft-Expiry.");
            }

            await PruefeBefundAsync(befunde, () => ErwarteAblaufHinweisAsync(app, "L4", "nach dem Agent-Start"));
            Rendezvous.Signal("t9-l4-b-bereit");
            await AlarmVonAAsync(app, "l4");

            DrueckeHotkey(HotkeyP2Neu);
            Rendezvous.Signal("t9-l4-p2-aus");
            var banner = app.ErwarteBanner("L4: P2 von B bei abgelaufener Lizenz (Soft-Expiry)");
            await Rendezvous.WaitForAsync("t9-l4-a-quittiert", SchrittTimeout);
            await app.SchliesseBannerAsync(banner);

            // L5
            await Lizenz.WechselnAsync("l5", "b", () => Lizenz.Einspielen(Lizenz.Gueltig), SchrittTimeout);
            Assert.Equal(LicenseStatus.Valid, Lizenz.Status().Status);
            await PruefeBefundAsync(befunde, () => WarteBisAsync(() => app.FindeToast(LimitToast) is null, NegativDauer,
                "Produktbefund L5: Sperr-Toast auf B trotz gültiger Lizenz."));
            MeldeBefunde(befunde);
        }, "t9");

    private async Task AlarmVonAAsync(HaelpMiApp app, string stufe)
    {
        await Rendezvous.WaitForAsync($"t9-{stufe}-p1-aus", SchrittTimeout);
        await app.QuittiereUndSchliesseAsync(app.ErwartePopup($"{stufe.ToUpperInvariant()}: P1 von A"), "B");
        Rendezvous.Signal($"t9-{stufe}-b-quittiert");
    }

    /// <summary>Hinweis-Toast auf dem User-Gerät bei abgelaufener Lizenz (Soft-Expiry: Alarme laufen weiter), danach schließen.</summary>
    private async Task ErwarteAblaufHinweisAsync(HaelpMiApp app, string stufe, string wann, TimeSpan? timeout = null)
    {
        Window? toast = null;
        var text = string.Empty;
        await WarteBisAsync(() => (toast = app.FindeToast(ErinnerungsToast)) is not null && (text = NameVon(toast, "MessageText")).Length > 0,
            timeout ?? NegativDauer, $"Produktbefund {stufe}: kein Hinweis ({ErinnerungsToast}) auf B bei abgelaufener Lizenz {wann}.");
        _ausgabe.WriteLine($"{stufe}: Hinweis auf B '{text}'");
        var passt = Regex.IsMatch(text, @"seit \d+ Tag\(en\) abgelaufen") && text.Contains("Alarme weiter", StringComparison.Ordinal);
        Element(toast!, "CloseButton").AsButton().Invoke();
        Assert.True(passt, $"Produktbefund {stufe}: Hinweis auf B zeigt '{text}', erwartet 'Die Lizenz ist seit … abgelaufen … Alarme weiter …'.");
    }
}
