using FlaUI.Core.AutomationElements;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T22: Senden ohne Empfänger.
public partial class GeraetATests
{
    /// <summary>
    /// Alarm-Profil ohne Empfänger für A: Das Tastenkürzel muss trotzdem sichtbar reagieren. Erwartet das
    /// Sender-Banner mit "Alarm nicht zugestellt", "Empfangen: 0 von 0" und einem Hinweis auf den Lösungsweg,
    /// genau eine Welle im Audit-Log und keine Wiederholung. Vor dem Fix passierte nichts.
    /// </summary>
    [Fact, TestPriority(155)]
    [Trait("Bereich", "R41"), Trait("FR", "FR-50"), Trait("FR", "FR-53")]
    public Task T22_Regression41_SendenOhneEmpfaenger() =>
        Artefakte.SichereAsync(nameof(T22_Regression41_SendenOhneEmpfaenger), async () =>
        {
            var a = EigeneDeviceId();
            // Version unter T10/T13 ist unkritisch: B hat denselben Stand, A zieht nichts nach.
            await BereiteAlarmprofilVorAsync(a, Array.Empty<Guid>(), 1400, 1, "E2E-Alarm ohne Empfänger");

            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            var seit = DateTimeOffset.UtcNow;
            DrueckeHotkey(AlarmHotkey);
            var banner = app.ErwarteBanner("Regression #41 (T22, Profil ohne Empfänger)");
            try
            {
                await WarteBisAsync(() => NameVon(banner, "StatusText") == "Alarm nicht zugestellt", TimeSpan.FromSeconds(10),
                    "T22: Das Banner meldet nicht \"Alarm nicht zugestellt\".");
                Assert.Equal("Empfangen: 0 von 0", NameVon(banner, "AckedText"));
                var hinweis = NameVon(banner, "NoRecipientsText");
                _ausgabe.WriteLine($"T22: Hinweis '{hinweis}'");
                Assert.Contains("kein Gerät", hinweis);
                Assert.Contains("Dashboard", hinweis);
                Assert.False(Element(banner, "CancelButton").IsEnabled, "T22: 'Abbrechen' ist noch aktiv, der Alarm läuft also weiter.");

                // Länger als zwei Wiederholungstakte (5 s): Es darf bei genau einer Welle bleiben.
                await Task.Delay(TimeSpan.FromSeconds(12));
                var wellen = AuditLogZeilen(seit).Count(z => z.Contains($"alarm sent alarmProfileId={ProfilId}", StringComparison.Ordinal));
                Assert.True(wellen == 1, $"T22: {wellen} Wellen im audit.log statt genau einer.");

                Element(banner, "CloseBannerButton").AsButton().Invoke();
                await WarteBisAsync(() => app.FindeSenderBanner() is null, TimeSpan.FromSeconds(10),
                    "T22: Das Sender-Banner schließt sich nach 'Schließen' nicht.");
            }
            finally
            {
                await app.SchliesseSenderBannerAsync();
            }
        });
}
