using System.Text.RegularExpressions;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Runtime;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T10 Regressionstest #136, Gegenstück und Auswertung in GeraetBTests.R136.cs.
public partial class GeraetATests
{
    /// <summary>
    /// Sender-Seite: pro Zyklus ein Alarm A an B (Schwelle 1). Nachdem B regulär geschlossen hat, schickt A
    /// dieselbe Session noch einmal direkt an B (Nachwelle, Variante W) - genau das, was eine verspätete
    /// letzte Repeat-Welle beim Empfänger bewirkt.
    /// </summary>
    [Fact, TestPriority(100)]
    [Trait("Teil", ZweiGeraete.Teil), Trait("Bereich", "R136")]
    public Task T10_Regression136_SendetWellenUndNachwelle() =>
        Artefakte.SichereAsync(nameof(T10_Regression136_SendetWellenUndNachwelle), async () =>
        {
            var a = EigeneDeviceId();
            var b = await DeviceIdDerGegenseiteAsync("b");
            // Eigene Version über allem, was T5 bis T9 per Config-Sync verteilt haben.
            await BereiteAlarmprofilVorAsync(a, b, 5000);
            await Rendezvous.WaitForAsync("t10-b-bereit", GegenseiteTimeout);
            await WarteAufGeraetAsync(b, DiscoveryTimeout);

            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            var zyklen = E2EUmgebung.Zyklen;
            for (var n = 1; n <= zyklen; n++)
            {
                var seit = DateTimeOffset.UtcNow;
                DrueckeHotkey(AlarmHotkey);
                Rendezvous.Signal($"t10-{n}-aus", $"{DateTimeOffset.UtcNow:O}");
                var banner = app.ErwarteBanner($"T10 Zyklus {n}");

                var zu = (await Rendezvous.WaitForAsync($"t10-{n}-zu", SchrittTimeout)).Trim();
                var sitzung = SitzungAusAudit(seit)
                    ?? throw new InvalidOperationException($"Zyklus {n}: audit.log auf A enthält keine Zeile 'alarm sent alarmProfileId={ProfilId} sessionId=…'.");

                // Nach einem Neustart auf B kennt dessen Agent die Session nicht mehr, eine Nachwelle sagte nichts aus.
                if (zu == "neustart")
                {
                    Rendezvous.Signal($"t10-{n}-nachwelle", $"übersprungen\n{sitzung}");
                }
                else
                {
                    var ergebnis = await SendeNachwelleAsync(b, sitzung);
                    Rendezvous.Signal($"t10-{n}-nachwelle", $"{sitzung}\n{ergebnis.AckedCount}/{ergebnis.TargetCount}\n{DateTimeOffset.UtcNow:O}");
                }

                await app.SchliesseBannerAsync(banner);
                await Rendezvous.WaitForAsync($"t10-{n}-fertig", SchrittTimeout);
            }
        }, "t10");

    private static Guid? SitzungAusAudit(DateTimeOffset seit)
    {
        var muster = new Regex($@"alarm sent alarmProfileId={ProfilId} sessionId=(?<id>[0-9a-fA-F-]{{36}})");
        return AuditLogZeilen(seit).Select(z => muster.Match(z)).LastOrDefault(m => m.Success) is { } treffer
            ? Guid.Parse(treffer.Groups["id"].Value)
            : null;
    }

    /// <summary>Gleiche Session, gleiches Profil, gleiche Identität wie der Agent (LiveIdentityFactory), also für B nicht von einer Repeat-Welle zu unterscheiden.</summary>
    private static async Task<AlarmSendResult> SendeNachwelleAsync(Guid empfaenger, Guid sitzung)
    {
        var settings = LiesEinstellungen() ?? throw new InvalidOperationException("settings.json nicht lesbar.");
        var profil = LiesConfig()?.AlarmProfiles.FirstOrDefault(p => p.Id == ProfilId)
            ?? throw new InvalidOperationException($"Profil {ProfilId} fehlt in shared-config.json.");
        var ziel = LiesGeraete().FirstOrDefault(d => d.DeviceId == empfaenger)
            ?? throw new InvalidOperationException($"Gerät {empfaenger} fehlt in devices.json.");

        return await new AlarmSender().SendAsync(profil, sitzung, LiveIdentityFactory.Create(settings, Lizenz.Deployment()), new[] { ziel });
    }
}
