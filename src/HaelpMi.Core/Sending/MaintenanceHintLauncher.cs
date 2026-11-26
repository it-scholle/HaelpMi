using System.ComponentModel;
using System.Diagnostics;
using HaelpMi.Core.Diagnostics;

namespace HaelpMi.Core.Sending;

/// <summary>
/// Startet das Hilfsprogramm <see cref="ExeName"/>, das den neutralen Wartungshinweis als
/// Windows-Benachrichtigung zeigt. Ein eigenes Programm, damit Windows als Absender dessen
/// Dateibeschreibung „Arbeitsplatz-Dienst“ nennt und der Agent seine Beschreibung behält.
/// </summary>
public sealed class MaintenanceHintLauncher
{
    public const string ExeName = "HaelpMi.Notify.exe";

    private readonly string _exePath;
    private readonly Func<ProcessStartInfo, Process?> _start;
    private readonly Action<string, Exception?> _warn;

    /// <param name="directory">Ordner, in dem das Hilfsprogramm neben dem Agent liegt.</param>
    /// <param name="start">Nur für Tests ersetzbar; Standard ist <see cref="Process.Start(ProcessStartInfo)"/>.</param>
    /// <param name="warn">Nur für Tests ersetzbar; Standard ist eine WARNUNG im Betriebs-Log.</param>
    public MaintenanceHintLauncher(string directory, Func<ProcessStartInfo, Process?>? start = null, Action<string, Exception?>? warn = null)
    {
        _exePath = Path.Combine(directory, ExeName);
        _start = start ?? Process.Start;
        _warn = warn ?? ((meldung, ausnahme) => BetriebsLog.Warnung("Alarm", meldung, ausnahme));
    }

    public ProcessStartInfo CreateStartInfo()
    {
        var info = new ProcessStartInfo(_exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(_exePath) ?? string.Empty,
        };
        info.ArgumentList.Add(SenderStatusTexts.MaintenanceHintTitle);
        info.ArgumentList.Add(SenderStatusTexts.MaintenanceHintText);
        return info;
    }

    /// <returns>true, wenn das Hilfsprogramm gestartet wurde; sonst steht eine WARNUNG im Betriebs-Log.</returns>
    public bool Launch()
    {
        try
        {
            using var process = _start(CreateStartInfo());
            if (process is null)
            {
                _warn($"Wartungshinweis nicht angezeigt: {ExeName} wurde nicht gestartet.", null);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _warn($"Wartungshinweis nicht angezeigt: {ExeName} ließ sich nicht starten ({_exePath}). Installation reparieren oder neu installieren.", ex);
            return false;
        }
    }
}
