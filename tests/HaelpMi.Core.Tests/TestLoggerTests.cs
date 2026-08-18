using System.Text.Json;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Covers das strukturierte Test-Aktionsprotokoll (Flaw 19): Gate (Installationsart + Log-
/// Level), Schema/Korrelation, Nicht-Blockieren bei Schreibfehlern, Rotation.
/// </summary>
public class TestLoggerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    // xUnit legt pro Testmethode eine neue Instanz an - Konstruktor räumt statischen
    // TestLogger-Zustand auf, den kein IDisposable-Hook abdeckt (MinLevel-Override,
    // Einmal-pro-Tag-Rotationsmarker), damit Tests unabhängig von Ausführungsreihenfolge sind.
    public TestLoggerTests()
    {
        TestLogger.ResetMinLevelOverrideForTests();
        TestLogger.ResetCleanupStateForTests();
    }

    private static string TodayFileName() => $"test-actions-{DateTime.UtcNow:yyyy-MM-dd}.jsonl";

    [Fact]
    public void LogAction_TestInstaller_WritesValidJsonLine()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        using var testInstall = TestLogger.ForceIsTestInstallerForTests(true);
        TestLogger.MinLevel = TestLogLevel.Info;

        var localDeviceId = Guid.NewGuid();
        var remoteDeviceId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        TestLogger.LogAction(
            TestLogEventType.AlarmActivated,
            TestLogLevel.Info,
            TestLogDirection.Local,
            localDeviceId,
            correlationId,
            remoteDeviceId,
            "Testdetail");

        var path = Path.Combine(AppPaths.RootFolder, TodayFileName());
        Assert.True(File.Exists(path));
        var lines = File.ReadAllLines(path);
        var line = Assert.Single(lines);

        var entry = JsonSerializer.Deserialize<TestLogEntryForAsserts>(line, JsonOptions)!;
        Assert.Equal("Info", entry.Level);
        Assert.Equal("AlarmActivated", entry.EventType);
        Assert.Equal("Local", entry.Direction);
        Assert.Equal(localDeviceId, entry.LocalDeviceId);
        Assert.Equal(remoteDeviceId, entry.RemoteDeviceId);
        Assert.Equal(correlationId, entry.CorrelationId);
        Assert.Equal("Testdetail", entry.Detail);
        // ms-Auflösung: "O"-Serialisierung von DateTimeOffset behält sie automatisch bei.
        Assert.Contains(".", entry.TimestampUtc);
    }

    [Fact]
    public void LogAction_ProductionInstall_DefaultsToErrorThreshold_NoDestinationYet()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        using var testInstall = TestLogger.ForceIsTestInstallerForTests(false);

        Assert.Equal(TestLogLevel.Error, TestLogger.MinLevel);

        // Selbst ein Error-Level-Aufruf schreibt (noch) nichts - produktiver Zielort ist
        // bewusst noch nicht implementiert (siehe TestLogger-Klassendoku).
        TestLogger.LogAction(TestLogEventType.AlarmActivated, TestLogLevel.Error, TestLogDirection.Local, Guid.NewGuid());

        Assert.False(Directory.Exists(AppPaths.RootFolder) && Directory.EnumerateFiles(AppPaths.RootFolder, "test-actions-*.jsonl").Any());
    }

    [Fact]
    public void LogAction_TestInstaller_DefaultInfoThreshold_LetsEverythingThrough()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        using var testInstall = TestLogger.ForceIsTestInstallerForTests(true);

        Assert.Equal(TestLogLevel.Info, TestLogger.MinLevel);
    }

    [Fact]
    public void LogAction_BelowMinLevel_IsSuppressed()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        using var testInstall = TestLogger.ForceIsTestInstallerForTests(true);
        TestLogger.MinLevel = TestLogLevel.Warn;

        TestLogger.LogAction(TestLogEventType.MessageSent, TestLogLevel.Info, TestLogDirection.Send, Guid.NewGuid());

        var path = Path.Combine(AppPaths.RootFolder, TodayFileName());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LogAction_SameCorrelationId_AcrossTwoCalls_IsTraceable()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        using var testInstall = TestLogger.ForceIsTestInstallerForTests(true);
        TestLogger.MinLevel = TestLogLevel.Info;

        var alarmSessionId = Guid.NewGuid();
        var senderDeviceId = Guid.NewGuid();
        var receiverDeviceId = Guid.NewGuid();

        TestLogger.LogAction(TestLogEventType.MessageSent, TestLogLevel.Info, TestLogDirection.Send, senderDeviceId, alarmSessionId, receiverDeviceId);
        TestLogger.LogAction(TestLogEventType.MessageReceived, TestLogLevel.Info, TestLogDirection.Receive, receiverDeviceId, alarmSessionId, senderDeviceId);

        var path = Path.Combine(AppPaths.RootFolder, TodayFileName());
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        var entries = lines.Select(l => JsonSerializer.Deserialize<TestLogEntryForAsserts>(l, JsonOptions)!).ToList();
        Assert.All(entries, e => Assert.Equal(alarmSessionId, e.CorrelationId));
    }

    [Fact]
    public void LogAction_DestinationUnwritable_NeverThrows()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        using var testInstall = TestLogger.ForceIsTestInstallerForTests(true);
        TestLogger.MinLevel = TestLogLevel.Info;

        // Legt am Zielpfad der heutigen Logdatei stattdessen ein Verzeichnis an - File.AppendAllText
        // schlägt dann fehl (UnauthorizedAccessException), ohne den Root-Ordner selbst anzufassen.
        Directory.CreateDirectory(Path.Combine(AppPaths.RootFolder, TodayFileName()));

        var exception = Record.Exception(() =>
            TestLogger.LogAction(TestLogEventType.AlarmActivated, TestLogLevel.Info, TestLogDirection.Local, Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public void LogAction_OldFiles_AreRotatedAway()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        using var testInstall = TestLogger.ForceIsTestInstallerForTests(true);
        TestLogger.MinLevel = TestLogLevel.Info;

        Directory.CreateDirectory(AppPaths.RootFolder);
        var oldFile = Path.Combine(AppPaths.RootFolder, "test-actions-2020-01-01.jsonl");
        File.WriteAllText(oldFile, "{}\n");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow - TimeSpan.FromDays(31));

        TestLogger.LogAction(TestLogEventType.AlarmActivated, TestLogLevel.Info, TestLogDirection.Local, Guid.NewGuid());

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(Path.Combine(AppPaths.RootFolder, TodayFileName())));
    }

    // Nur zum Deserialisieren in Tests - Feldnamen müssen zu TestLogEntry passen, das selbst
    // internal ist und deshalb hier nicht direkt referenziert werden kann.
    private sealed record TestLogEntryForAsserts(
        string TimestampUtc,
        string ProcessName,
        string Level,
        string EventType,
        string Direction,
        Guid LocalDeviceId,
        Guid? RemoteDeviceId,
        Guid? CorrelationId,
        string? Detail);
}
