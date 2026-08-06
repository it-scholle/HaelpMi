using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Agent and Config are separate processes that both read/write the same settings.json
/// / devices.json with no shared lock - a real install surfaced exactly this: Config
/// opening the device list right as the Agent was mid-write threw an unhandled
/// IOException and took the whole Konfigurationsfenster down. These pin down that
/// JsonFileStore.Load retries through a transient lock instead of propagating.
/// </summary>
public class JsonFileStoreTests
{
    private sealed record Sample(string Name);

    [Fact]
    public void Load_RetriesThroughATransientExclusiveLock_AndEventuallySucceeds()
    {
        using var scope = new TestAppDataScope();
        var path = Path.Combine(Path.GetTempPath(), "HaelpMiTests_" + Guid.NewGuid().ToString("N") + ".json");
        JsonFileStore.Save(path, new Sample("Empfang EG"));

        try
        {
            // Hold an exclusive (FileShare.None) lock on a background thread for a short
            // while, simulating the other process being mid-write, then release it -
            // Load() must wait it out rather than throw. Two events make this
            // deterministic: Load() only starts once the lock is confirmed held, so the
            // test actually exercises the retry path instead of racing it.
            using var lockAcquired = new ManualResetEventSlim(false);
            using var releaseLock = new ManualResetEventSlim(false);
            var lockThread = new Thread(() =>
            {
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                lockAcquired.Set();
                releaseLock.Wait(TimeSpan.FromSeconds(5));
            });
            lockThread.Start();

            Assert.True(lockAcquired.Wait(TimeSpan.FromSeconds(5)));
            var releaseTimer = new Timer(_ => releaseLock.Set(), null, dueTime: 500, period: Timeout.Infinite);

            var result = JsonFileStore.Load<Sample>(path);

            lockThread.Join();
            releaseTimer.Dispose();
            Assert.NotNull(result);
            Assert.Equal("Empfang EG", result!.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_RetriesThroughATransientExclusiveLock_AndEventuallySucceeds()
    {
        // Save() geht denselben Retry-Weg wie Load() oben, aber über File.Replace statt
        // File.Open - eine bestehende Datei, die die jeweils andere Seite (Agent/Config)
        // gerade kurz exklusiv offen hält (z. B. mitten im eigenen Load()), darf einen
        // Save() nicht mit einer unbehandelten IOException abbrechen lassen.
        using var scope = new TestAppDataScope();
        var path = Path.Combine(Path.GetTempPath(), "HaelpMiTests_" + Guid.NewGuid().ToString("N") + ".json");
        JsonFileStore.Save(path, new Sample("Alt"));

        try
        {
            using var lockAcquired = new ManualResetEventSlim(false);
            using var releaseLock = new ManualResetEventSlim(false);
            var lockThread = new Thread(() =>
            {
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                lockAcquired.Set();
                releaseLock.Wait(TimeSpan.FromSeconds(5));
            });
            lockThread.Start();

            Assert.True(lockAcquired.Wait(TimeSpan.FromSeconds(5)));
            var releaseTimer = new Timer(_ => releaseLock.Set(), null, dueTime: 500, period: Timeout.Infinite);

            JsonFileStore.Save(path, new Sample("Neu"));

            lockThread.Join();
            releaseTimer.Dispose();

            var reloaded = JsonFileStore.Load<Sample>(path);
            Assert.NotNull(reloaded);
            Assert.Equal("Neu", reloaded!.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        // Directory.CreateDirectory(...) in Save() ist der einzige Grund, warum
        // settings.json/devices.json auf einer frischen Installation überhaupt geschrieben
        // werden können - %ProgramData%\HaelpMi existiert vor der Ersteinrichtung nicht.
        using var scope = new TestAppDataScope();
        var dir = Path.Combine(Path.GetTempPath(), "HaelpMiTests_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "sample.json");
        Assert.False(Directory.Exists(dir));

        try
        {
            JsonFileStore.Save(path, new Sample("Erstanlage"));

            Assert.True(Directory.Exists(dir));
            Assert.Equal("Erstanlage", JsonFileStore.Load<Sample>(path)!.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsNull_WhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), "HaelpMiTests_" + Guid.NewGuid().ToString("N") + ".json");
        Assert.False(File.Exists(path));

        Assert.Null(JsonFileStore.Load<Sample>(path));
    }
}
