namespace HaelpMi.Core.Diagnostics;

/// <summary>Einstellungen für <see cref="BetriebsLog"/>; die Standardwerte sind die Grenzen aus AGENTS.md Teil B.</summary>
/// <param name="Ordner">Ablageordner aller Dateien dieses Logs.</param>
/// <param name="Name">Fester Dateiname ohne Endung, je Prozess verschieden (z. B. „agent“).</param>
public sealed record BetriebsLogOptionen(string Ordner, string Name)
{
    public LogStufe MinStufe { get; init; } = LogStufe.Info;

    public long MaxDateiBytes { get; init; } = BetriebsLog.StandardMaxDateiBytes;

    /// <summary>Summe aller Dateien dieses Logs (aktuell, Teile, Archive).</summary>
    public long MaxGesamtBytes { get; init; } = BetriebsLog.StandardMaxGesamtBytes;

    /// <summary>Archive älter als so viele Tage werden gelöscht. 90 statt 30: das Log enthält keine personenbezogenen Daten.</summary>
    public int AufbewahrungTage { get; init; } = 90;

    /// <summary>Archive ab diesem Alter in Tagen werden mit GZip komprimiert.</summary>
    public int KomprimierenAbTagen { get; init; } = 7;

    public TimeProvider Zeit { get; init; } = TimeProvider.System;
}
