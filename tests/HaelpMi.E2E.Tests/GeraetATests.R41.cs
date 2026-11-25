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
    /// Alarm-Profil ohne Empfänger für A: Das Tastenkürzel muss trotzdem sichtbar reagieren (Owner-Entscheidung 29,
    /// Entwurf B2). Erwartet das gewohnte Sender-Popup sofort mit "Kein Empfänger eingestellt!" und dem Info-Hinweis
    /// auf das Dashboard, ohne Zähler und ohne "Abbrechen"; es blendet sich nach einigen Sekunden selbst aus. Im
    /// Audit-Log genau eine Welle, keine Wiederholung. Den späteren Wartungshinweis (Windows-Benachrichtigung) prüft
    /// der Handtestplan. Vor #41 passierte nichts, vor B2 meldete das Popup "Alarm nicht zugestellt".
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
                await WarteBisAsync(() => NameVon(banner, "StatusText") == "Kein Empfänger eingestellt!", TimeSpan.FromSeconds(5),
                    () => $"T22: Das Popup meldet '{NameVon(banner, "StatusText")}' statt \"Kein Empfänger eingestellt!\".");
                Assert.Equal("Info: Ein Admin muss im Dashboard Empfänger für dieses Profil einrichten.", SichtbarerText(banner, "NoRecipientsText"));
                Assert.True(SichtbarerText(banner, "CancelButton") == string.Empty, "T22: Das Popup zeigt einen Abbrechen-Knopf.");
                Assert.True(SichtbarerText(banner, "AckedText") == string.Empty,
                    $"T22: Das Popup zeigt den Zähler '{SichtbarerText(banner, "AckedText")}'.");

                await WarteBisAsync(() => app.FindeSenderBanner() is null, TimeSpan.FromSeconds(15),
                    "T22: Das Popup blendet sich nicht von selbst aus.");

                // Länger als zwei Wiederholungstakte (5 s): Es darf bei genau einer Welle bleiben.
                var rest = seit + TimeSpan.FromSeconds(12) - DateTimeOffset.UtcNow;
                if (rest > TimeSpan.Zero)
                {
                    await Task.Delay(rest);
                }
                var wellen = AuditLogZeilen(seit).Count(z => z.Contains($"alarm sent alarmProfileId={ProfilId}", StringComparison.Ordinal));
                Assert.True(wellen == 1, $"T22: {wellen} Wellen im audit.log statt genau einer.");
            }
            finally
            {
                await app.SchliesseSenderBannerAsync();
            }
        });
}
