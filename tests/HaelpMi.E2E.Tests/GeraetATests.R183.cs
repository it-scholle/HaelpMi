using HaelpMi.Core.Licensing;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T14 Regressionstest #183, Gegenstück in GeraetBTests.R183.cs.
public partial class GeraetATests
{
    /// <summary>
    /// A hat die gültige Lizenz, B eine später ausgestellte, aber abgelaufene. Nach dem Lizenz-Gossip (Boot-Call von B
    /// an den laufenden Agent von A, dessen Antwort an B) behält A die gültige Lizenz.
    /// </summary>
    [Fact, TestPriority(120)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R183")]
    public Task T14_Regression183_GueltigeLizenzBleibt() =>
        Artefakte.SichereAsync(nameof(T14_Regression183_GueltigeLizenzBleibt), async () =>
        {
            // Vor dem Agent-Start, damit auch eine Übernahme beim ersten Boot-Call-Austausch im audit.log zählt.
            var seit = DateTimeOffset.UtcNow;
            await Lizenz.WechselnAsync("vorher", "a", () => Lizenz.Einspielen(Lizenz.Gueltig), GegenseiteTimeout, "t14");
            Rendezvous.Signal("t14-a-bereit");
            var meldungB = (await Rendezvous.WaitForAsync("t14-b-gossip", SchrittTimeout)).Trim();

            var status = Lizenz.Status();
            var uebernahme = AuditLogZeilen(seit).FirstOrDefault(z => z.Contains("Lizenz von Peer", StringComparison.Ordinal));
            _ausgabe.WriteLine($"T14: Lizenz auf A {status.Status}, ausgestellt {status.License?.IssuedAtUtc:O}; Audit '{uebernahme}'; B meldet '{meldungB}'");

            // Gültige Lizenz auf beiden Geräten, auch wenn die Prüfung unten scheitert.
            await Lizenz.WechselnAsync("nachher", "a", () => Lizenz.Einspielen(Lizenz.Gueltig), SchrittTimeout, "t14");

            Assert.True(status.Status == LicenseStatus.Valid && uebernahme is null,
                $"Produktbefund: A hat die gültige Lizenz durch die abgelaufene von B ersetzt (Status {status.Status}, Audit '{uebernahme}').");
        }, "t14");
}
