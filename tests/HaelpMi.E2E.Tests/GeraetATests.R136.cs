using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Runtime;
using HaelpMi.Installer.Tests;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;
using static HaelpMi.E2E.Tests.ZweiGeraete;

namespace HaelpMi.E2E.Tests;

// T10, T18 und T19 (Alarmfenster öffnet nach dem Schließen erneut), Gegenstücke und Auswertung in GeraetBTests.R136.cs und GeraetCTests.cs.
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

    /// <summary>
    /// T18, Sender-Seite: wie T10 Variante N, aber A alarmiert B und C mit Schwelle 2 (eigenes Profil, Config-Version
    /// <see cref="DreiGeraete.ConfigVersionR136"/>). B und C melden "Bin unterwegs" im Versatz zur Repeat-Welle und
    /// schließen; A wartet pro Zyklus auf beide und meldet dann die Session-ID.
    /// </summary>
    [Fact, TestPriority(57)]
    [Trait("Teil", DreiGeraete.Teil), Trait("Bereich", "R136")]
    public async Task T18_Regression136_DreiGeraete_SendetWellen()
    {
        try
        {
            await T18_Rumpf();
        }
        catch
        {
            // Wie bei T12: erst weiter, wenn C seinen Agent beendet hat, sonst läuft C in T6 hinein.
            try
            {
                await Rendezvous.WaitForAsync(DreiGeraete.CGestopptT18, SchrittTimeout);
            }
            catch (TimeoutException ex)
            {
                _ausgabe.WriteLine($"T18: {ex.Message}");
            }

            throw;
        }
    }

    private Task T18_Rumpf() =>
        Artefakte.SichereAsync(nameof(T18_Regression136_DreiGeraete_SendetWellen), async () =>
        {
            var (_, b, c) = await DreiGeraete.BereiteAlarmprofilVorAsync(DreiGeraete.ConfigVersionR136);
            await Rendezvous.WaitForAsync("t18-b-bereit", GegenseiteTimeout);
            await Rendezvous.WaitForAsync("t18-c-bereit", GegenseiteTimeout);
            await WarteAufGeraetAsync(b, DiscoveryTimeout);
            await WarteAufGeraetAsync(c, DiscoveryTimeout);

            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            var zyklen = E2EUmgebung.Zyklen;
            for (var n = 1; n <= zyklen; n++)
            {
                var seit = DateTimeOffset.UtcNow;
                DrueckeHotkey(AlarmHotkey);
                Rendezvous.Signal($"t18-{n}-aus", $"{DateTimeOffset.UtcNow:O}");
                var banner = app.ErwarteBanner($"T18 Zyklus {n}");
                try
                {
                    await Rendezvous.WaitForAsync($"t18-{n}-b-zu", SchrittTimeout);
                    await Rendezvous.WaitForAsync($"t18-{n}-c-zu", SchrittTimeout);
                }
                catch
                {
                    // Sonst wiederholt A den Alarm bis in T6 hinein.
                    BrichAlarmAb(banner);
                    await app.SchliesseSenderBannerAsync();
                    throw;
                }

                var sitzung = SitzungAusAudit(seit)
                    ?? throw new InvalidOperationException($"Zyklus {n}: audit.log auf A enthält keine Zeile 'alarm sent alarmProfileId={ProfilId} sessionId=…'.");

                // Hat ein Empfänger nach einem Neustart nicht gemeldet, alarmiert A sonst bis zum Zeitlimit weiter.
                if (BrichAlarmAb(banner))
                {
                    _ausgabe.WriteLine($"Zyklus {n}: Alarm lief nach beiden Rückmeldungen noch, abgebrochen.");
                    await app.SchliesseSenderBannerAsync();
                }
                else
                {
                    await app.SchliesseBannerAsync(banner);
                }

                Rendezvous.Signal($"t18-{n}-fertig", sitzung.ToString());
            }

            // C beendet danach seinen Agent; ab T6 soll C nicht mehr mitspielen (siehe GeraetCTests).
            await Rendezvous.WaitForAsync(DreiGeraete.CGestopptT18, SchrittTimeout);
        }, "t18");

    /// <summary>
    /// T19, Sender-Seite: Alarm A an B und C (Schwelle 2, Config-Version <see cref="DreiGeraete.ConfigVersionMehrfach"/>).
    /// Nachdem beide "Bin unterwegs" gemeldet und geschlossen haben, schickt A <see cref="R136Mehrfach.MinWellen"/> bis
    /// <see cref="R136Mehrfach.MaxWellen"/> weitere Wellen derselben Session an beide, jeweils nach 0,5 bis 2 s.
    /// </summary>
    [Fact, TestPriority(58)]
    [Trait("Teil", DreiGeraete.Teil), Trait("Teil", R136Mehrfach.Teil), Trait("Bereich", "R136")]
    public async Task T19_Regression136_MehrfachNachwelle_SendetWellen()
    {
        try
        {
            await T19_Rumpf();
        }
        catch
        {
            try
            {
                await Rendezvous.WaitForAsync(DreiGeraete.CGestopptT19, SchrittTimeout);
            }
            catch (TimeoutException ex)
            {
                _ausgabe.WriteLine($"T19: {ex.Message}");
            }

            throw;
        }
    }

    private Task T19_Rumpf() =>
        Artefakte.SichereAsync(nameof(T19_Regression136_MehrfachNachwelle_SendetWellen), async () =>
        {
            var (_, b, c) = await DreiGeraete.BereiteAlarmprofilVorAsync(DreiGeraete.ConfigVersionMehrfach);
            await Rendezvous.WaitForAsync("t19-b-bereit", GegenseiteTimeout);
            await Rendezvous.WaitForAsync("t19-c-bereit", GegenseiteTimeout);
            await WarteAufGeraetAsync(b, DiscoveryTimeout);
            await WarteAufGeraetAsync(c, DiscoveryTimeout);

            using var app = new HaelpMiApp();
            await app.SchliesseSenderBannerAsync();
            var zyklen = E2EUmgebung.Zyklen;
            for (var n = 1; n <= zyklen; n++)
            {
                var seit = DateTimeOffset.UtcNow;
                DrueckeHotkey(AlarmHotkey);
                Rendezvous.Signal($"t19-{n}-aus", $"{DateTimeOffset.UtcNow:O}");
                var banner = app.ErwarteBanner($"T19 Zyklus {n}");
                string zuB, zuC;
                try
                {
                    zuB = (await Rendezvous.WaitForAsync($"t19-{n}-b-zu", SchrittTimeout)).Trim();
                    zuC = (await Rendezvous.WaitForAsync($"t19-{n}-c-zu", SchrittTimeout)).Trim();
                }
                catch
                {
                    BrichAlarmAb(banner);
                    await app.SchliesseSenderBannerAsync();
                    throw;
                }

                var sitzung = SitzungAusAudit(seit)
                    ?? throw new InvalidOperationException($"Zyklus {n}: audit.log auf A enthält keine Zeile 'alarm sent alarmProfileId={ProfilId} sessionId=…'.");

                // Nach einem Neustart kennt der Empfänger die Session nicht mehr, Nachwellen sagten nichts aus.
                if (zuB == "neustart" || zuC == "neustart")
                {
                    Rendezvous.Signal($"t19-{n}-wellen", $"übersprungen\n{sitzung}");
                }
                else
                {
                    var (anzahl, abstaende) = R136Mehrfach.Wellenplan(n);
                    var acks = new List<string>();
                    foreach (var abstand in abstaende)
                    {
                        await Task.Delay(abstand);
                        var ergebnis = await SendeNachwelleAsync(new[] { b, c }, sitzung);
                        acks.Add($"{ergebnis.AckedCount}/{ergebnis.TargetCount}");
                    }

                    Rendezvous.Signal($"t19-{n}-wellen", $"{sitzung}\n{anzahl}\n{string.Join(',', acks)}\n{string.Join(',', abstaende)}");
                    _ausgabe.WriteLine($"Zyklus {n}: {anzahl} Nachwellen ({string.Join(',', acks)}) nach {string.Join(',', abstaende)} ms.");
                }

                if (BrichAlarmAb(banner))
                {
                    _ausgabe.WriteLine($"Zyklus {n}: Alarm lief nach beiden Rückmeldungen noch, abgebrochen.");
                    await app.SchliesseSenderBannerAsync();
                }
                else
                {
                    await app.SchliesseBannerAsync(banner);
                }

                await Rendezvous.WaitForAsync($"t19-{n}-b-fertig", SchrittTimeout);
                await Rendezvous.WaitForAsync($"t19-{n}-c-fertig", SchrittTimeout);
            }

            // C beendet danach seinen Agent; ab T6 soll C nicht mehr mitspielen (siehe GeraetCTests).
            await Rendezvous.WaitForAsync(DreiGeraete.CGestopptT19, SchrittTimeout);
        }, "t19");

    /// <returns>true, wenn der Alarm noch lief und abgebrochen wurde; das Banner schließt sich dann selbst.</returns>
    private static bool BrichAlarmAb(Window banner)
    {
        try
        {
            var abbrechen = Element(banner, "CancelButton").AsButton();
            if (!abbrechen.IsEnabled)
            {
                return false;
            }

            abbrechen.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false;
        }
    }

    private static Guid? SitzungAusAudit(DateTimeOffset seit)
    {
        var muster = new Regex($@"alarm sent alarmProfileId={ProfilId} sessionId=(?<id>[0-9a-fA-F-]{{36}})");
        return AuditLogZeilen(seit).Select(z => muster.Match(z)).LastOrDefault(m => m.Success) is { } treffer
            ? Guid.Parse(treffer.Groups["id"].Value)
            : null;
    }

    /// <summary>Gleiche Session, gleiches Profil, gleiche Identität wie der Agent (LiveIdentityFactory), also für B nicht von einer Repeat-Welle zu unterscheiden.</summary>
    private static Task<AlarmSendResult> SendeNachwelleAsync(Guid empfaenger, Guid sitzung) =>
        SendeNachwelleAsync(new[] { empfaenger }, sitzung);

    private static async Task<AlarmSendResult> SendeNachwelleAsync(IReadOnlyList<Guid> empfaenger, Guid sitzung)
    {
        var settings = LiesEinstellungen() ?? throw new InvalidOperationException("settings.json nicht lesbar.");
        var profil = LiesConfig()?.AlarmProfiles.FirstOrDefault(p => p.Id == ProfilId)
            ?? throw new InvalidOperationException($"Profil {ProfilId} fehlt in shared-config.json.");
        var geraete = LiesGeraete();
        var ziele = empfaenger.Select(id => geraete.FirstOrDefault(d => d.DeviceId == id)
            ?? throw new InvalidOperationException($"Gerät {id} fehlt in devices.json.")).ToList();

        return await new AlarmSender().SendAsync(profil, sitzung, LiveIdentityFactory.Create(settings, Lizenz.Deployment()), ziele);
    }
}
