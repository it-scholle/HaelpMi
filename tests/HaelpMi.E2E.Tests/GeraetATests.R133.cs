using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T13 Regressionstest #133, Gegenstück in GeraetBTests.R133.cs.
public partial class GeraetATests
{
    private static readonly TimeSpan BannerBeobachtung = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Admin deaktiviert B im Dashboard: Das rote Banner "Lizenzkontingent überschritten" darf nicht erscheinen,
    /// eine manuelle Deaktivierung ist keine Lizenzüberschreitung. Danach B wieder aktivieren.
    /// </summary>
    [Fact, TestPriority(130)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R133")]
    public Task T13_Regression133_DeaktivierenOhneLizenzbanner() =>
        Artefakte.SichereAsync(nameof(T13_Regression133_DeaktivierenOhneLizenzbanner), async () =>
        {
            var b = await DeviceIdDerGegenseiteAsync("b");
            await WarteAufGeraetAsync(b, DiscoveryTimeout);
            await Rendezvous.WaitForAsync("t13-b-bereit", GegenseiteTimeout);

            using var app = new HaelpMiApp();
            var seite = DashboardSeite.Oeffne(app);
            seite.WaehleTabPerId("DevicesTabItem");
            Assert.True(SichtbaresLizenzbanner(seite) is null,
                $"Vorbedingung: das Lizenzbanner ist schon vor dem Deaktivieren sichtbar ('{SichtbaresLizenzbanner(seite)}').");

            var schalter = await GeraeteSchalterAsync(seite, b);
            Assert.True(schalter.AsToggleButton().ToggleState == ToggleState.On, "B ist vor dem Test nicht aktiv.");
            schalter.Click();
            await WarteBisAsync(() => UeberschreibungVon(b) == LicenseOverride.ForceDisabled, TimeSpan.FromSeconds(10),
                () => $"devices.json: B hat nach dem Deaktivieren LicenseOverride={UeberschreibungVon(b)} statt ForceDisabled.");
            Rendezvous.Signal("t13-aus");

            var ende = DateTime.UtcNow + BannerBeobachtung;
            while (DateTime.UtcNow < ende)
            {
                if (SichtbaresLizenzbanner(seite) is { } titel)
                {
                    Assert.Fail($"Nach dem manuellen Deaktivieren von B zeigt das Dashboard das Lizenzbanner '{titel}'.");
                }

                await Task.Delay(300);
            }

            await Rendezvous.WaitForAsync("t13-b-geprueft", SchrittTimeout);
            seite.Fenster.SetForeground();
            (await GeraeteSchalterAsync(seite, b)).Click();
            await WarteBisAsync(() => UeberschreibungVon(b) is { } wert && wert != LicenseOverride.ForceDisabled, TimeSpan.FromSeconds(10),
                () => $"devices.json: B hat nach dem Aktivieren LicenseOverride={UeberschreibungVon(b)}.");
            BeendeKonfiguration();
            Rendezvous.Signal("t13-an");
            await Rendezvous.WaitForAsync("t13-b-fertig", SchrittTimeout);
        }, "t13");

    /// <summary>Titel des Lizenzbanners, falls sichtbar. Sucht auch per Text, weil ältere Stände den Titel ohne AutomationId zeigen.</summary>
    private static string? SichtbaresLizenzbanner(DashboardSeite seite)
    {
        var titel = new[]
        {
            LicenseBlockTexts.DashboardBannerTitle(LicenseBlockReason.LimitExceeded),
            LicenseBlockTexts.DashboardBannerTitle(LicenseBlockReason.NoLicense),
        };
        var element = seite.Fenster.FindFirstDescendant(cf => cf.ByAutomationId("LicenseLimitBannerTitle"))
            ?? titel.Select(t => seite.Fenster.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text).And(cf.ByName(t))))
                .FirstOrDefault(e => e is not null);
        return element is not null && !element.IsOffscreen ? element.Name : null;
    }
}
