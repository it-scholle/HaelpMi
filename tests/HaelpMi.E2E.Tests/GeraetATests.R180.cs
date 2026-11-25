using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using HaelpMi.Core.Models;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T16 Regressionstest #180, Gegenstück in GeraetBTests.R180.cs.
public partial class GeraetATests
{
    /// <summary>
    /// A deaktiviert B im Reiter "Geräte / Lizenz" und aktiviert es wieder. Danach steht B in devices.json von A auf
    /// LicenseOverride None, nicht ForceEnabled: über die Aktivierung entscheidet wieder das Lizenzkontingent.
    /// </summary>
    [Fact, TestPriority(140)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R180")]
    public Task T16_Regression180_AktivierenSetztKeinForceEnabled() =>
        Artefakte.SichereAsync(nameof(T16_Regression180_AktivierenSetztKeinForceEnabled), async () =>
        {
            var b = await DeviceIdDerGegenseiteAsync("b");
            using var app = new HaelpMiApp();
            var seite = DashboardSeite.Oeffne(app);
            seite.WaehleTabPerId("DevicesTabItem");

            var schalter = await GeraeteSchalterAsync(seite, b);
            if (schalter.AsToggleButton().ToggleState != ToggleState.On)
            {
                throw new InvalidOperationException($"Vorbedingung fehlt: B ist vor T16 nicht aktiv (LicenseOverride={UeberschreibungVon(b)}).");
            }

            schalter.Click();
            await WarteBisAsync(() => UeberschreibungVon(b) == LicenseOverride.ForceDisabled, TimeSpan.FromSeconds(10),
                () => $"devices.json: B hat nach dem Deaktivieren LicenseOverride={UeberschreibungVon(b)} statt ForceDisabled.");

            seite.Fenster.SetForeground();
            (await GeraeteSchalterAsync(seite, b)).Click();
            await WarteBisAsync(() => UeberschreibungVon(b) is { } wert && wert != LicenseOverride.ForceDisabled, TimeSpan.FromSeconds(10),
                () => $"devices.json: B hat nach dem Aktivieren LicenseOverride={UeberschreibungVon(b)}.");
            var nachher = UeberschreibungVon(b);
            _ausgabe.WriteLine($"T16: LicenseOverride von B nach dem Aktivieren: {nachher}");
            BeendeKonfiguration();
            Rendezvous.Signal("t16-a-aktiviert", $"{nachher}");

            var meldungB = (await Rendezvous.WaitForAsync("t16-b-fertig", SchrittTimeout)).Trim();
            _ausgabe.WriteLine($"T16: B meldet '{meldungB}'");

            Assert.True(nachher == LicenseOverride.None,
                $"Produktbefund: Aktivieren setzt LicenseOverride={nachher} statt None; B umgeht damit das Lizenzkontingent.");
        }, "t16");
}
