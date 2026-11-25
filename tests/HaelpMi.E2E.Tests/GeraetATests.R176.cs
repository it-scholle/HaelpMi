using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using HaelpMi.Core.Storage;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;

namespace HaelpMi.E2E.Tests;

// T21 Regressionstest #176.
public partial class GeraetATests
{
    /// <summary>
    /// Selbsttest direkt nach der Installation, noch ohne Alarm-Profil: Der Agent lehnt ab, das Einstellungsfenster
    /// muss den Grund samt Lösungsweg zeigen und auf dem Admin-Gerät das Dashboard anbieten. Vor dem Fix passierte nichts.
    /// </summary>
    [Fact, TestPriority(25)]
    [Trait("Bereich", "R176"), Trait("FR", "FR-27")]
    public Task T21_Regression176_SelbsttestOhneProfilMeldetGrund() =>
        Artefakte.SichereAsync(nameof(T21_Regression176_SelbsttestOhneProfilMeldetGrund), async () =>
        {
            Assert.True(new SharedConfigStore().LoadOrCreate().AlarmProfiles.Count == 0,
                "Vorbedingung: T21 läuft direkt nach T1 auf einem frisch installierten Gerät ohne Alarm-Profil.");
            BeendeKonfiguration();

            using var app = new HaelpMiApp();
            var einstellungen = app.OeffneEinstellungen(FensterTimeout);
            Element(einstellungen, "SelfTestButton").AsButton().Invoke();

            AutomationElement? dialog = null;
            await WarteBisAsync(() => (dialog = app.FindeDialogUeberall(KonfigProzess)) is not null, TimeSpan.FromSeconds(15),
                "Regression #176: Selbsttest ohne Alarm-Profil zeigt keine Meldung.");
            var text = string.Join(" ", dialog!.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Select(t => t.Name));
            _ausgabe.WriteLine($"T21: Meldung '{text}'");
            Assert.Contains("Alarm-Profil", text);
            Assert.Contains("Dashboard", text);
            Assert.Contains("jetzt öffnen", text);
            Assert.False(app.AlarmfensterOffen(), "Ohne Alarm-Profil darf der Selbsttest kein Alarmfenster öffnen.");

            // "Ja" (IDYES = 6) muss direkt zum Ort der Lösung führen.
            var ja = dialog.FindFirstDescendant(cf => cf.ByAutomationId("6"))
                ?? throw new InvalidOperationException("Regression #176: Die Meldung bietet das Admin-Dashboard nicht an (Knopf 'Ja' fehlt).");
            ja.AsButton().Invoke();
            var dashboard = app.WarteAufFenster(KonfigProzess, TitelDashboard, null, FensterTimeout);
            Assert.False(dashboard.IsOffscreen, "Admin-Dashboard ist nach 'Ja' nicht sichtbar.");

            BeendeKonfiguration();
        });
}
