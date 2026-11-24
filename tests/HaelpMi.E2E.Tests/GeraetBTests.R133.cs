using FlaUI.Core.AutomationElements;
using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T15 Regressionstest #133, Gegenstück in GeraetATests.R133.cs.
public partial class GeraetBTests
{
    private static readonly TimeSpan HinweisTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Vom Admin deaktiviert: Der Toast auf B nennt die Deaktivierung als Grund, nicht ein ausgeschöpftes Lizenzkontingent.</summary>
    [Fact, TestPriority(130)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R133")]
    public Task T15_Regression133_HinweisNenntDeaktivierung() =>
        Artefakte.SichereAsync(nameof(T15_Regression133_HinweisNenntDeaktivierung), async () =>
        {
            var a = await DeviceIdDerGegenseiteAsync("a");
            await WarteAufGeraetAsync(a, DiscoveryTimeout);
            using var app = new HaelpMiApp();
            Window? Toast() => app.FindeToast("LicenseLimitToastWindow");
            Assert.True(Toast() is null, $"Vorbedingung: B zeigt schon vor dem Deaktivieren den Sperr-Toast ('{TextVon(Toast())}').");
            Rendezvous.Signal("t15-b-bereit");

            await Rendezvous.WaitForAsync("t15-aus", GegenseiteTimeout);
            await WarteBisAsync(() => LiesEinstellungen()?.LicenseOverride == LicenseOverride.ForceDisabled, HinweisTimeout,
                () => $"settings.json auf B: LicenseOverride={LiesEinstellungen()?.LicenseOverride} statt ForceDisabled.");

            var titel = LicenseBlockTexts.Title(LicenseBlockReason.ManuallyDisabled);
            var text = LicenseBlockTexts.Message(LicenseBlockReason.ManuallyDisabled);
            await WarteBisAsync(() => Toast() is { } t && NameVon(t, "TitleText") == titel && NameVon(t, "MessageText") == text, HinweisTimeout,
                () => $"Sperr-Toast auf B nennt nicht die Deaktivierung: '{TextVon(Toast())}', erwartet '{titel}: {text}'.");

            _ausgabe.WriteLine($"Toast auf B: '{TextVon(Toast())}'");
            Rendezvous.Signal("t15-b-geprueft");

            await Rendezvous.WaitForAsync("t15-an", SchrittTimeout);
            await WarteBisAsync(() => Toast() is null, HinweisTimeout, "Der Sperr-Toast bleibt nach dem Aktivieren auf B stehen.");
            Rendezvous.Signal("t15-b-fertig");
        }, "t15");

    private static string TextVon(Window? toast) =>
        toast is null ? "-" : $"{NameVon(toast, "TitleText")}: {NameVon(toast, "MessageText")}";
}
