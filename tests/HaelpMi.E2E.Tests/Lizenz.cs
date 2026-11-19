using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;

namespace HaelpMi.E2E.Tests;

/// <summary>Lizenzdateien und -zustand für T9. Die Varianten erzeugt ci/e2e/testlizenz-erzeugen.ps1 neben lizenz.json.</summary>
internal static class Lizenz
{
    public const string Gueltig = "lizenz.json";
    public const string Bald = "lizenz-bald.json";
    public const string Abgelaufen = "lizenz-abgelaufen.json";

    public static string Quelle(string datei)
    {
        var basis = E2EUmgebung.LizenzPfad ?? throw new InvalidOperationException("HAELPMI_E2E_LIZENZ ist nicht gesetzt.");
        var pfad = Path.Combine(Path.GetDirectoryName(basis)!, datei);
        return File.Exists(pfad) ? pfad : throw new FileNotFoundException("Testlizenz fehlt im Installer-Artefakt.", pfad);
    }

    public static DeploymentInfo Deployment() =>
        JsonFileStore.Load<DeploymentInfo>(Path.Combine(E2EUmgebung.ProgrammOrdner, "deployment.json"))
        ?? throw new InvalidOperationException("deployment.json fehlt.");

    public static LicenseCheckResult Status()
    {
        var deployment = Deployment();
        return LicenseReader.Load(deployment.CustomerGroupId, Convert.FromHexString(deployment.LicensePublicKeyHex));
    }

    /// <summary>Lizenzschlüssel als Text, wie ihn ein Admin aus einer E-Mail ins Dashboard einfügt.</summary>
    public static string Schluesseltext(string datei) =>
        LicenseKeyText.Encode(JsonFileStore.Load<License>(Quelle(datei)) ?? throw new InvalidOperationException($"{datei} nicht lesbar."));

    /// <summary>Aktive und vorgemerkte Lizenz sowie "Später erinnern" entfernen.</summary>
    public static void Entfernen()
    {
        foreach (var datei in new[] { AppPaths.LicenseFilePath, AppPaths.PendingLicenseFilePath, AppPaths.LicenseReminderStateFilePath })
        {
            File.Delete(datei);
        }
    }

    public static void Einspielen(string datei)
    {
        Entfernen();
        File.Copy(Quelle(datei), AppPaths.LicenseFilePath, overwrite: true);
    }

    /// <summary>
    /// Agent auf beiden Geräten erst nach der Dateiänderung beider Seiten starten: ein früher gestartetes Gerät
    /// übernähme sonst per Gossip die Lizenz der Gegenseite, sobald deren IssuedAtUtc neuer ist (TryAdoptFromPeer).
    /// </summary>
    /// <param name="stufe">Teil des Markennamens, z. B. "l3".</param>
    /// <param name="geraet">"a" oder "b".</param>
    public static async Task WechselnAsync(string stufe, string geraet, Action aenderung, TimeSpan timeout)
    {
        HaelpMiApp.BeendeAlles();
        aenderung();
        Rendezvous.Signal($"t9-{stufe}-{geraet}-gewechselt");
        await Rendezvous.WaitForAsync($"t9-{stufe}-{(geraet == "a" ? "b" : "a")}-gewechselt", timeout);
        HaelpMiApp.StarteAgentUeberAutostart();
        await HaelpMiApp.WarteAufAgentAsync(TimeSpan.FromSeconds(60));
        await Task.Delay(ZweiGeraete.AgentAnlaufzeit);
    }

    /// <summary>
    /// Testvorbereitung: nach T8 steht B auf ForceEnabled (Wieder-Aktivieren setzt das statt None) und wäre damit
    /// auch ohne Lizenz immer aktiv. Nur bei gestopptem Agent aufrufen, sonst überschreibt er die Datei.
    /// </summary>
    public static void EigeneUeberschreibungZuruecksetzen()
    {
        var settings = JsonFileStore.Load<OwnSettings>(AppPaths.SettingsFilePath) ?? throw new InvalidOperationException("settings.json fehlt.");
        settings.LicenseOverride = LicenseOverride.None;
        settings.LicenseOverrideSetAtUtc = DateTimeOffset.UtcNow;
        JsonFileStore.Save(AppPaths.SettingsFilePath, settings);
    }

    /// <summary>Gegenstück zu <see cref="EigeneUeberschreibungZuruecksetzen"/> in devices.json von A.</summary>
    public static void UeberschreibungZuruecksetzen(Guid deviceId)
    {
        var store = new DeviceStore();
        var geraete = store.Load();
        DeviceStore.SetLicenseOverride(geraete, deviceId, LicenseOverride.None, DateTimeOffset.UtcNow);
        store.Save(geraete);
    }
}
