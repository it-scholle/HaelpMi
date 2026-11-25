using FlaUI.Core.AutomationElements;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T22 Regressionstest #41.
public partial class GeraetATests
{
    /// <summary>
    /// Alarm-Profil ohne Empfänger für A: Das Tastenkürzel muss trotzdem einen Alarm starten. Erwartet das
    /// Sender-Banner mit "Empfangen: 0 von 0", einem Hinweis auf den Lösungsweg und funktionierendem Abbrechen.
    /// Vor dem Fix passierte nichts.
    /// </summary>
    [Fact, TestPriority(160)]
    [Trait("Bereich", "R41"), Trait("FR", "FR-50"), Trait("FR", "FR-53")]
    public Task T22_Regression41_SendenOhneEmpfaenger() =>
        Artefakte.SichereAsync(nameof(T22_Regression41_SendenOhneEmpfaenger), async () =>
        {
            var a = EigeneDeviceId();
            await BereiteAlarmprofilVorAsync(a, Array.Empty<Guid>(), 1400, 1, "E2E-Alarm ohne Empfänger");

            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            var seit = DateTimeOffset.UtcNow;
            DrueckeHotkey(AlarmHotkey);
            var banner = app.ErwarteBanner("Regression #41 (T22, Profil ohne Empfänger)");
            try
            {
                Assert.Equal("Empfangen: 0 von 0", NameVon(banner, "AckedText"));
                var hinweis = NameVon(banner, "NoRecipientsText");
                _ausgabe.WriteLine($"T22: Hinweis '{hinweis}'");
                Assert.Contains("keine Empfänger", hinweis);
                Assert.Contains("Dashboard", hinweis);
                Assert.Contains(AuditLogZeilen(seit), z => z.Contains($"alarm sent alarmProfileId={ProfilId}", StringComparison.Ordinal));

                Element(banner, "CancelButton").AsButton().Invoke();
                await WarteBisAsync(() => app.FindeSenderBanner() is null, TimeSpan.FromSeconds(10),
                    "T22: Das Sender-Banner schließt sich nach 'Abbrechen' nicht.");
            }
            finally
            {
                await app.SchliesseSenderBannerAsync();
            }
        });
}
