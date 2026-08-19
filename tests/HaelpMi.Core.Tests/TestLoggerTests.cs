using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Sending;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Covers das strukturierte Test-Aktionsprotokoll: Gate (Installationsart + Log-Level,
/// Flaw 19), Schema/Korrelation, Nicht-Blockieren bei Schreibfehlern, Rotation, sowie
/// (<see cref="LogAction_RealAlarmSendReceiveRoundTrip_ProducesCorrelatableLines"/>) die
/// tatsächliche Instrumentierung der Kommunikationsschicht (Flaw 20).
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

    /// <summary>
    /// Flaw 20: ein echter TCP-Sende-/Empfangs-/Ack-Durchlauf (AlarmSender -&gt; AlarmTcpListener,
    /// wie AlarmFlowCoordinatorTests.SendSelfTestAsync_MarksOutgoingRequestAsTest, hier aber
    /// direkt über die Bausteine statt über den Coordinator) muss mindestens AlarmActivated
    /// (hier simuliert durch den direkten SendAsync-Aufruf), MessageSent, MessageReceived,
    /// AckSent, AckReceived mit DERSELBEN CorrelationId erzeugen - das ist die Kernzusage aus
    /// Flaw 20 Punkt 2 ("Ablauf ausschließlich aus dem Log rekonstruierbar").
    /// </summary>
    [Fact]
    public async Task LogAction_RealAlarmSendReceiveRoundTrip_ProducesCorrelatableLines()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        using var testInstall = TestLogger.ForceIsTestInstallerForTests(true);
        TestLogger.MinLevel = TestLogLevel.Info;

        var senderDeviceId = Guid.NewGuid();
        var receiverDeviceId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(Guid.NewGuid(), senderDeviceId, "Sender-PC", "Frau Test", "Zimmer", "1", Role.User, false, "0.0.0", 0);
        var receiverIdentity = new LiveIdentity(senderIdentity.CustomerGroupId, receiverDeviceId, "Empf-PC", "Herr Novak", "Raum", "2", Role.User, false, "0.0.0", 0);

        var freePort = GetFreeTcpPort();
        var listener = new AlarmTcpListener(() => receiverIdentity);
        listener.Start(freePort);
        try
        {
            var target = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = freePort };
            var profile = new AlarmProfile { Text = "Testalarm", ResponseThreshold = 1 };
            var alarmSessionId = Guid.NewGuid();

            var result = await new AlarmSender().SendAsync(profile, alarmSessionId, senderIdentity, new[] { target });
            Assert.Equal(1, result.AckedCount);

            var path = Path.Combine(AppPaths.RootFolder, TodayFileName());
            var lines = File.ReadAllLines(path);
            var entries = lines.Select(l => JsonSerializer.Deserialize<TestLogEntryForAsserts>(l, JsonOptions)!).ToList();
            var forThisAlarm = entries.Where(e => e.CorrelationId == alarmSessionId).ToList();

            Assert.Contains(forThisAlarm, e => e.EventType == "MessageSent" && e.Direction == "Send");
            Assert.Contains(forThisAlarm, e => e.EventType == "MessageReceived" && e.Direction == "Receive");
            Assert.Contains(forThisAlarm, e => e.EventType == "AckSent" && e.Direction == "Send");
            Assert.Contains(forThisAlarm, e => e.EventType == "AckReceived" && e.Direction == "Receive");
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    private static int GetFreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
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
