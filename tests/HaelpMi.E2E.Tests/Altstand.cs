using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HaelpMi.E2E.Tests;

/// <summary>
/// Läufe mit <c>produkt_ref</c> installieren einen älteren Produktstand. Teilprüfungen für Features, die es dort noch
/// nicht gibt, würden sonst jeden Lauf gegen den Altstand rot machen und Negativtests unmöglich. Ohne <c>produkt_ref</c>
/// wird immer alles geprüft.
/// </summary>
internal static class Altstand
{
    /// <summary>Firewall-Programmregeln „HälpMi Agent“/„HälpMi Konfiguration“ aus dem Installer (#150).</summary>
    public static readonly Merkmal Programmregeln = new("Firewall-Programmregeln des Installers", new Version(0, 47, 26));

    /// <summary>Regeln vor dem ersten Agent-Start, keine Block-Regel nach einer Windows-Abfrage (#222).</summary>
    public static readonly Merkmal RegelnVorErstemStart = new("Firewall-Regeln vor dem ersten Start, keine Block-Regel", new Version(0, 47, 28));

    /// <summary>Betriebs-Log agent.log (#227).</summary>
    public static readonly Merkmal BetriebsLog = new("Betriebs-Log agent.log", new Version(0, 47, 35));

    public sealed record Merkmal(string Name, Version Ab);

    /// <summary>Datei im Artefakt des Geräts, je übersprungener Prüfung eine Zeile; der Workflow zeigt sie in der Summary.</summary>
    public static string ProtokollDatei => Path.Combine(Artefakte.Ordner, "uebersprungen.txt");

    /// <summary>
    /// true, wenn <paramref name="test"/> die Prüfung für <paramref name="merkmal"/> ausführen muss. Übersprungen wird nur
    /// mit <c>produkt_ref</c>, bei lesbarer Agent-Version unter <see cref="Merkmal.Ab"/> und wenn der Test nicht selbst in
    /// <c>erwartet_rot</c> steht (dort zählt jede Prüfung, sonst wäre ein Negativtest für genau dieses Merkmal unmöglich).
    /// </summary>
    /// <param name="test">T-Kürzel ohne Fehlerbild-Buchstaben, z. B. „T1“.</param>
    public static bool Pruefen(string test, Merkmal merkmal)
    {
        if (E2EUmgebung.ProduktRef is null || ErwartetRot().Contains(test))
        {
            return true;
        }

        var version = AgentVersion();
        if (version is null || version >= merkmal.Ab)
        {
            return true;
        }

        Directory.CreateDirectory(Artefakte.Ordner);
        File.AppendAllText(ProtokollDatei,
            $"{test}: übersprungen: {merkmal.Name} – Feature erst ab v{merkmal.Ab}, installiert ist Agent v{version} " +
            $"(PRODUKT_REF={E2EUmgebung.ProduktRef}){Environment.NewLine}");
        return false;
    }

    /// <summary>Major.Minor.Build der installierten HaelpMi.Agent.exe, null wenn nicht lesbar (dann wird geprüft).</summary>
    public static Version? AgentVersion()
    {
        var exe = Path.Combine(E2EUmgebung.ProgrammOrdner, HaelpMiApp.AgentProzess + ".exe");
        if (!File.Exists(exe))
        {
            return null;
        }

        var info = FileVersionInfo.GetVersionInfo(exe);
        return info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0
            ? null
            : new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
    }

    private static HashSet<string> ErwartetRot()
    {
        var wert = Environment.GetEnvironmentVariable("HAELPMI_E2E_ERWARTET_ROT") ?? "";
        return wert.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => Regex.Replace(k, "[A-Z]$", ""))
            .ToHashSet(StringComparer.Ordinal);
    }
}
