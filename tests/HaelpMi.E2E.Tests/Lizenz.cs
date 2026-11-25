using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;

namespace HaelpMi.E2E.Tests;

/// <summary>Lizenzdateien und -zustand für T9, T14 und T17. Die Varianten erzeugt ci/e2e/testlizenz-erzeugen.ps1 neben lizenz.json.</summary>
internal static class Lizenz
{
    public const string Gueltig = "lizenz.json";
    public const string Bald = "lizenz-bald.json";
    public const string Abgelaufen = "lizenz-abgelaufen.json";

    /// <summary>Später ausgestellt als <see cref="Gueltig"/>, aber schon abgelaufen (T14).</summary>
    public const string AbgelaufenNeuer = "lizenz-abgelaufen-neuer.json";

    /// <summary>
    /// T17: unter lizenz-kurz/ die Lizenz, die als nächste, aber frühestens nach <paramref name="vorlauf"/> abläuft
    /// (ci/e2e/testlizenz-erzeugen.ps1 legt sie gestaffelt an). Rückgabe relativ zum Ordner von <see cref="Gueltig"/>.
    /// </summary>
    public static (string Datei, DateTime AblaufUtc) KurzWaehlen(TimeSpan vorlauf)
    {
        var ordner = Path.Combine(Path.GetDirectoryName(Quelle(Gueltig))!, "lizenz-kurz");
        var frueheste = DateTime.UtcNow + vorlauf;
        var auswahl = Directory.EnumerateFiles(ordner, "lizenz-kurz-*.json")
            .Select(pfad => (Datei: Path.Combine("lizenz-kurz", Path.GetFileName(pfad)), Lizenz: JsonFileStore.Load<License>(pfad)))
            .Where(l => l.Lizenz is not null && l.Lizenz.ExpiryDateUtc >= frueheste)
            .OrderBy(l => l.Lizenz!.ExpiryDateUtc)
            .FirstOrDefault();
        return auswahl.Lizenz is null
            ? throw new InvalidOperationException($"Keine Lizenz unter {ordner} läuft frühestens {frueheste:O} ab; der Build liegt zu lange zurück (testlizenz-erzeugen.ps1 -KurzStunden).")
            : (auswahl.Datei, auswahl.Lizenz.ExpiryDateUtc);
    }

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
    /// <param name="test">Markenpräfix des aufrufenden Tests, z. B. "t14".</param>
    public static async Task WechselnAsync(string stufe, string geraet, Action aenderung, TimeSpan timeout, string test = "t9")
    {
        HaelpMiApp.BeendeAlles();
        aenderung();
        Rendezvous.Signal($"{test}-{stufe}-{geraet}-gewechselt");
        await Rendezvous.WaitForAsync($"{test}-{stufe}-{(geraet == "a" ? "b" : "a")}-gewechselt", timeout);
        await HaelpMiApp.StarteAgentAsync();
        await Task.Delay(ZweiGeraete.AgentAnlaufzeit);
    }
}
