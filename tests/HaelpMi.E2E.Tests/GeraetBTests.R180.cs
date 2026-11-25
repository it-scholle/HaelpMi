using HaelpMi.Core.Models;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T16 Regressionstest #180, Gegenstück und Ablauf auf A in GeraetATests.R180.cs.
public partial class GeraetBTests
{
    /// <summary>B übernimmt per Gossip erst ForceDisabled, nach dem Wieder-Aktivieren durch A dann LicenseOverride None in settings.json.</summary>
    [Fact, TestPriority(140)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R180")]
    public Task T16_Regression180_EigenerOverrideNone() =>
        Artefakte.SichereAsync(nameof(T16_Regression180_EigenerOverrideNone), async () =>
        {
            await Rendezvous.WaitForAsync("t16-a-aus", GegenseiteTimeout);
            await WarteBisAsync(() => LiesEinstellungen()?.LicenseOverride == LicenseOverride.ForceDisabled, NegativDauer,
                () => $"settings.json auf B: LicenseOverride={LiesEinstellungen()?.LicenseOverride} statt ForceDisabled nach dem Deaktivieren.");
            Rendezvous.Signal("t16-b-aus");

            await Rendezvous.WaitForAsync("t16-a-aktiviert", SchrittTimeout);
            var befunde = new List<string>();
            await PruefeBefundAsync(befunde, () => WarteBisAsync(() => LiesEinstellungen()?.LicenseOverride == LicenseOverride.None, NegativDauer,
                () => $"Produktbefund: settings.json auf B steht nach dem Aktivieren auf LicenseOverride={LiesEinstellungen()?.LicenseOverride} statt None."));
            var eigener = LiesEinstellungen()?.LicenseOverride;
            _ausgabe.WriteLine($"T16: eigener LicenseOverride auf B: {eigener}");
            Rendezvous.Signal("t16-b-fertig", $"{eigener}");
            MeldeBefunde(befunde);
        }, "t16");
}
