using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T17 Regressionstest #181, Gegenstück in GeraetBTests.R181.cs.
public partial class GeraetATests
{
    /// <summary>Vorlauf vor dem Ablauf: Agent-Start und Anlauf auf beiden Geräten müssen vorher fertig sein.</summary>
    private static readonly TimeSpan T17Vorlauf = TimeSpan.FromMinutes(3);

    /// <summary>
    /// A und B bekommen eine Lizenz, die wenige Minuten nach dem Agent-Start abläuft. Ohne Neustart zeigt A danach den
    /// Erinnerungs-Toast "Lizenz seit … abgelaufen" (B prüft seinen eigenen Hinweis). Danach beide wieder lizenz.json.
    /// </summary>
    [Fact, TestPriority(150)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R181")]
    public Task T17_Regression181_AblaufOhneNeustartErkannt() =>
        Artefakte.SichereAsync(nameof(T17_Regression181_AblaufOhneNeustartErkannt), async () =>
        {
            var (datei, ablauf) = Lizenz.KurzWaehlen(T17Vorlauf);
            _ausgabe.WriteLine($"T17: {datei}, Ablauf {ablauf:O}");
            Rendezvous.Signal("t17-datei", datei);
            await Lizenz.WechselnAsync("kurz", "a", () => Lizenz.Einspielen(datei), GegenseiteTimeout, "t17");

            var befunde = new List<string>();
            using (var app = new HaelpMiApp())
            {
                // Beim Start "läuft in 0 Tag(en) ab"; schließen, damit der spätere Hinweis eindeutig neu ist.
                Window? toast = null;
                try
                {
                    toast = app.WarteAuf(() => app.FindeToast(ErinnerungsToast), "Erinnerungs-Toast beim Start", TimeSpan.FromSeconds(20));
                }
                catch (TimeoutException)
                {
                    befunde.Add("Produktbefund: kein Erinnerungs-Toast auf A beim Start mit bald ablaufender Lizenz.");
                }

                if (toast is not null)
                {
                    _ausgabe.WriteLine($"T17: Toast beim Start '{NameVon(toast, "MessageText")}'");
                    Element(toast, "CloseButton").AsButton().Invoke();
                    await WarteBisAsync(() => app.FindeToast(ErinnerungsToast) is null, TimeSpan.FromSeconds(5), "Erinnerungs-Toast auf A schließt sich nicht.");
                }

                var pid = AgentPid();
                var text = string.Empty;
                await PruefeBefundAsync(befunde, () => WarteBisAsync(
                    () => app.FindeToast(ErinnerungsToast) is { } t && Regex.IsMatch(text = NameVon(t, "MessageText"), @"^Lizenz seit \d+ Tag\(en\) abgelaufen"),
                    ablauf - DateTime.UtcNow + TimeSpan.FromSeconds(60),
                    () => $"Produktbefund: A zeigt bis 60 s nach dem Lizenzablauf ({ablauf:O}) ohne Neustart keinen Hinweis 'Lizenz seit … abgelaufen' (zuletzt '{text}')."));
                _ausgabe.WriteLine($"T17: Hinweis nach dem Ablauf '{text}'");
                if (AgentPid() != pid)
                {
                    befunde.Add($"Der Agent auf A wurde während T17 neu gestartet (PID {pid} -> {AgentPid()}); der Nachweis gilt nur ohne Neustart.");
                }

                if (app.FindeToast(ErinnerungsToast) is { } offen)
                {
                    Element(offen, "CloseButton").AsButton().Invoke();
                }
            }

            Rendezvous.Signal("t17-a-fertig");
            var meldungB = (await Rendezvous.WaitForAsync("t17-b-fertig", SchrittTimeout + (ablauf - DateTime.UtcNow))).Trim();
            _ausgabe.WriteLine($"T17: B meldet '{meldungB}'");

            await Lizenz.WechselnAsync("nachher", "a", () => Lizenz.Einspielen(Lizenz.Gueltig), SchrittTimeout, "t17");
            MeldeBefunde(befunde);
        }, "t17");
}
