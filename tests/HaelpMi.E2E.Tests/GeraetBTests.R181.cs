using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T17 Regressionstest #181, Gegenstück und Auswahl der Lizenz in GeraetATests.R181.cs.
public partial class GeraetBTests
{
    /// <summary>B (User-Gerät) zeigt ohne Neustart einen Hinweis, sobald die Lizenz abläuft; Alarme laufen weiter (Soft-Expiry).</summary>
    [Fact, TestPriority(150)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R181")]
    public Task T17_Regression181_HinweisOhneNeustart() =>
        Artefakte.SichereAsync(nameof(T17_Regression181_HinweisOhneNeustart), async () =>
        {
            var datei = (await Rendezvous.WaitForAsync("t17-datei", GegenseiteTimeout)).Trim();
            await Lizenz.WechselnAsync("kurz", "b", () => Lizenz.Einspielen(datei), GegenseiteTimeout, "t17");
            var ablauf = Lizenz.Status().License?.ExpiryDateUtc ?? throw new InvalidOperationException($"{datei} ist auf B nicht lesbar.");

            var befunde = new List<string>();
            using (var app = new HaelpMiApp())
            {
                var pid = AgentPid();
                await PruefeBefundAsync(befunde, () => ErwarteAblaufHinweisAsync(app, "T17", "ohne Neustart",
                    ablauf - DateTime.UtcNow + TimeSpan.FromSeconds(60)));
                if (AgentPid() != pid)
                {
                    befunde.Add($"Der Agent auf B wurde während T17 neu gestartet (PID {pid} -> {AgentPid()}); der Nachweis gilt nur ohne Neustart.");
                }
            }

            Rendezvous.Signal("t17-b-fertig", befunde.Count == 0 ? "Hinweis ok" : string.Join(" | ", befunde));
            await Rendezvous.WaitForAsync("t17-a-fertig", SchrittTimeout);
            await Lizenz.WechselnAsync("nachher", "b", () => Lizenz.Einspielen(Lizenz.Gueltig), SchrittTimeout, "t17");
            MeldeBefunde(befunde);
        }, "t17");
}
