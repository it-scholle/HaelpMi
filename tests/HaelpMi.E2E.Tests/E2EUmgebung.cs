using HaelpMi.Core.Models;

namespace HaelpMi.E2E.Tests;

/// <summary>Pfade und Umgebungsvariablen eines E2E-Laufs auf einer Test-VM.</summary>
internal static class E2EUmgebung
{
    /// <summary>Arbeitsordner des Laufs; enthält rv\ (Rendezvous) und out\ (Artefakte). Übersteuerbar per HAELPMI_E2E_DIR.</summary>
    public static string Wurzel => Env("HAELPMI_E2E_DIR") ?? @"C:\haelpmi-e2e";

    /// <summary>Vollständiger Pfad zur Installer-exe für dieses Gerät (A: Admin-Installer, B und C: User-Installer).</summary>
    public static string InstallerPfad => Env("HAELPMI_E2E_INSTALLER")
        ?? throw new InvalidOperationException("HAELPMI_E2E_INSTALLER ist nicht gesetzt (Pfad zur Installer-exe für dieses Gerät).");

    /// <summary>Optional: lizenz.json der Test-Kundengruppe dieses Laufs, erzeugt vom Workflow (ci/e2e/testlizenz-erzeugen.ps1).</summary>
    public static string? LizenzPfad => Env("HAELPMI_E2E_LIZENZ");

    /// <summary>Tag oder Commit, aus dem Agent und Installer dieses Laufs gebaut wurden; null = Stand des PR (Workflow-Input produkt_ref).</summary>
    public static string? ProduktRef => Env("HAELPMI_E2E_PRODUKT_REF");

    /// <summary>Anzahl Zyklen für T10 (Workflow-Input zyklen), Default 25.</summary>
    public static int Zyklen => int.TryParse(Env("HAELPMI_E2E_ZYKLEN"), out var n) && n > 0 ? n : 25;

    public static string Raum =>Env("HAELPMI_E2E_RAUM") ?? "E2E-A";

    public static string Raumnummer => Env("HAELPMI_E2E_RAUMNUMMER") ?? "101";

    /// <summary>{autopf}\HaelpMi aus HaelpMi.iss/HaelpMi-Admin.iss (DefaultDirName).</summary>
    public static string ProgrammOrdner => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HaelpMi");

    public static string DatenOrdner => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppConstants.AppDataFolderName);

    /// <summary>SharedLogPaths schreibt bevorzugt nach Z:\HaelpMi-Logs\&lt;Rechner&gt;, sonst nach <see cref="DatenOrdner"/>.</summary>
    public static IEnumerable<string> LogOrdner()
    {
        yield return DatenOrdner;
        yield return Path.Combine(@"Z:\", "HaelpMi-Logs", Environment.MachineName);
    }

    private static string? Env(string name)
    {
        var wert = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(wert) ? null : wert;
    }
}
