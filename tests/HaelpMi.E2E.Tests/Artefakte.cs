using FlaUI.Core.Capturing;

namespace HaelpMi.E2E.Tests;

/// <summary>Sammelt bei einem Fehlschlag Screenshot und HälpMi-Logs nach &lt;Wurzel&gt;\out, von wo der Workflow sie hochlädt.</summary>
internal static class Artefakte
{
    public static string Ordner => Path.Combine(E2EUmgebung.Wurzel, "out");

    // xUnit 2 hat keinen Fehlschlag-Hook, deshalb umschließt jeder Test seinen Rumpf hiermit.
    public static async Task SichereAsync(string testName, Func<Task> rumpf)
    {
        try
        {
            await rumpf();
        }
        catch
        {
            Sammle(testName);
            throw;
        }
    }

    public static string Sammle(string testName)
    {
        var ziel = Path.Combine(Ordner, $"{DateTime.Now:yyyyMMdd-HHmmss}-{testName}");
        Directory.CreateDirectory(ziel);

        Versuche(ziel, () => Capture.Screen().ToFile(Path.Combine(ziel, "bildschirm.png")));

        foreach (var ordner in E2EUmgebung.LogOrdner())
        {
            Versuche(ziel, () => KopiereDateien(ordner, "*.log", ziel));
        }

        Versuche(ziel, () => KopiereDateien(E2EUmgebung.DatenOrdner, "*.json", Path.Combine(ziel, "daten")));
        Versuche(ziel, () => File.WriteAllText(Path.Combine(ziel, "netzwerk.txt"), Prozess.PowerShell(NetzwerkDiagnose)));
        Versuche(ziel, () => KopiereDateien(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HaelpMi-uninstall-diagnostics.log", ziel));

        return ziel;
    }

    // Der User-Installer legt keine Firewall-Regeln an, der Admin-Installer nur für 51500/51501;
    // Feedback (51505) und Config-Sync (51502/51503) haben keine Regel.
    private const string NetzwerkDiagnose = """
        '--- Firewall-Profile ---'
        Get-NetFirewallProfile | Format-Table Name, Enabled, DefaultInboundAction -AutoSize | Out-String -Width 200
        '--- Regeln HälpMi* ---'
        Get-NetFirewallRule -DisplayName 'HälpMi*' -ErrorAction SilentlyContinue | ForEach-Object {
            $f = $_ | Get-NetFirewallPortFilter
            '{0} | {1} | {2} | {3} | {4}/{5}' -f $_.DisplayName, $_.Enabled, $_.Direction, $_.Action, $f.Protocol, $f.LocalPort
        }
        '--- Programmregeln HaelpMi* (z. B. nach einer Windows-Firewall-Abfrage) ---'
        Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue | Where-Object Program -like '*HaelpMi*' | ForEach-Object {
            $r = $_ | Get-NetFirewallRule
            '{0} | {1} | {2} | {3} | {4}' -f $r.DisplayName, $r.Enabled, $r.Direction, $r.Action, $_.Program
        }
        '--- Ports 51500-51506 ---'
        netstat -ano | Select-String ':5150[0-6]\s'
        """;

    private static void KopiereDateien(string quelle, string muster, string ziel)
    {
        if (!Directory.Exists(quelle))
        {
            return;
        }

        foreach (var datei in Directory.GetFiles(quelle, muster))
        {
            Directory.CreateDirectory(ziel);
            File.Copy(datei, Path.Combine(ziel, Path.GetFileName(datei)), overwrite: true);
        }
    }

    // Das Einsammeln darf den eigentlichen Testfehler nie überdecken.
    private static void Versuche(string ziel, Action aktion)
    {
        try
        {
            aktion();
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(ziel, "sammelfehler.txt"), ex + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }
    }
}
