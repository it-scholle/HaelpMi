using System.IO;
using System.Windows.Threading;

namespace HaelpMi.UI.Helpers;

/// <summary>
/// Beobachtet eine einzelne Datei auf Änderungen und meldet sich - entprellt und auf den
/// UI-Thread marshalled - über <see cref="Changed"/> (Nutzerwunsch 05.08.2026: "Konfigurator
/// und Dashboard sollen automatisch aktualisieren, wenn ein neuer Boot-Call sie erreicht
/// oder sie antworten"). Agent und Config/Dashboard sind getrennte Prozesse (siehe
/// JsonFileStore-Klassenkommentar) - ein In-Process-Ereignis wie
/// <c>DiscoveryService.DeviceUpdated</c> läuft nur im Agent-Prozess und erreicht das
/// Dashboard nie direkt. Die gemeinsame JSON-Datei unter %ProgramData%\HaelpMi ist der
/// einzige prozessübergreifende Signalweg, der dafür bereits existiert.
/// </summary>
public sealed class FileChangeWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _debounceTimer;

    public event EventHandler? Changed;

    public FileChangeWatcher(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? throw new ArgumentException("Kein Verzeichnisanteil im Pfad.", nameof(filePath));
        var fileName = Path.GetFileName(filePath);

        // JsonFileStore.Save schreibt über eine temporäre Datei + File.Replace (mehrere
        // einzelne FS-Events pro Speichervorgang, u. a. Renamed statt nur Changed) - der
        // Debounce-Timer fasst das zu einem einzigen Reload zusammen, statt mehrfach direkt
        // hintereinander neu zu laden.
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
        };
        _watcher.Changed += (_, _) => ScheduleReload();
        _watcher.Created += (_, _) => ScheduleReload();
        _watcher.Renamed += (_, _) => ScheduleReload();
        _watcher.EnableRaisingEvents = true;
    }

    private void ScheduleReload()
    {
        // FileSystemWatcher-Callbacks laufen auf einem ThreadPool-Thread, der DispatcherTimer
        // gehört aber dem UI-Thread des jeweiligen Fensters - Invoke statt direktem Zugriff.
        _debounceTimer.Dispatcher.Invoke(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounceTimer.Stop();
    }
}
