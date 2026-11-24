using HaelpMi.Core.Licensing;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T14 Regressionstest #183, Gegenstück und Prüfung auf A in GeraetATests.R183.cs.
public partial class GeraetBTests
{
    /// <summary>B startet mit einer später ausgestellten, aber abgelaufenen Lizenz und übernimmt die gültige von A.</summary>
    [Fact, TestPriority(120)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R183")]
    public Task T14_Regression183_UebernimmtGueltigeLizenz() =>
        Artefakte.SichereAsync(nameof(T14_Regression183_UebernimmtGueltigeLizenz), async () =>
        {
            var befunde = new List<string>();
            // Status vor dem Agent-Start prüfen: danach kann B die Lizenz von A schon beim ersten Boot-Call-Austausch übernommen haben.
            await Lizenz.WechselnAsync("vorher", "b", () =>
            {
                Lizenz.Einspielen(Lizenz.AbgelaufenNeuer);
                Assert.Equal(LicenseStatus.Expired, Lizenz.Status().Status);
            }, GegenseiteTimeout, "t14");

            // Zusätzlicher Neustart, wenn A sicher läuft: der Boot-Call von B erreicht dann den Agent von A, dessen Antwort trägt die Lizenz von A.
            await Rendezvous.WaitForAsync("t14-a-bereit", SchrittTimeout);
            await AgentNeustartMitAnlaufAsync();
            await PruefeBefundAsync(befunde, () => WarteBisAsync(() => Lizenz.Status().Status == LicenseStatus.Valid, TimeSpan.FromSeconds(60),
                () => $"Produktbefund: B übernimmt die gültige Lizenz von A nicht binnen 60 s (Status {Lizenz.Status().Status})."));
            var status = Lizenz.Status();
            _ausgabe.WriteLine($"T14: Lizenz auf B {status.Status}, ausgestellt {status.License?.IssuedAtUtc:O}");
            Rendezvous.Signal("t14-b-gossip", $"{status.Status}");

            await Lizenz.WechselnAsync("nachher", "b", () => Lizenz.Einspielen(Lizenz.Gueltig), SchrittTimeout, "t14");
            MeldeBefunde(befunde);
        }, "t14");
}
