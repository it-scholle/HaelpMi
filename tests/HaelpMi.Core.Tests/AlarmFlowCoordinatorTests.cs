using System.Reflection;
using HaelpMi.Agent;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Sending;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Prio 0.2 (15.08.2026, vor dem Verschlüsselungs-Task): bislang einziger unveränderter
/// Pipeline-Code seit v0.20.0 ohne eigene Testklasse - deckt den seit v0.26.0 vorhandenen
/// Audit-Push-Hook nach Alarmende ab (<see cref="AlarmFlowCoordinator"/>, private
/// RunSessionAsync-Methode), damit eine spätere versehentliche Änderung an dieser Stelle
/// (z. B. während der Verschlüsselungsarbeit) sichtbar fehlschlägt statt lautlos zu
/// regressieren.
///
/// Einstieg per Reflection direkt auf die private RunSessionAsync-Methode (gleiches Muster
/// wie SendingTests.cs für OnMyWayReceived) statt über TriggerAlarmProfile/
/// HandleIncomingAlarmRequest, deren Audit-Push- bzw. Popup-Ablauf hier nicht das Testziel ist.
/// SendSelfTestAsync selbst wird seit dem Fehlerbericht "Sende-Bubble erscheint nicht"
/// (18.08.2026, s. <see cref="SendSelfTestAsync_MarksOutgoingRequestAsTest"/>) direkt getestet -
/// der Dispatcher.Invoke-Block dort fängt eine im headless Testlauf fehlende
/// System.Windows.Application.Current inzwischen selbst ab (try/catch, geloggt statt geworfen),
/// der Audit-Push-Teil bleibt trotzdem fire-and-forget und damit weiterhin nicht darüber prüfbar.
///
/// Beide Tests laufen bewusst über den echten RepeatingAlarmSession.RunAsync()-Ablauf inkl.
/// des fest verdrahteten, nicht abkürzbaren 1-Minuten-Nachlauf-Fensters
/// (AppConstants.AlarmAutoCloseAfterLastSignal) - daher ~70-90s Laufzeit pro Testfall
/// (Nutzerentscheidung 15.08.2026: voller Pfad statt schnellerem, aber weniger
/// aussagekräftigem isoliertem Hook-Test).
/// </summary>
public class AlarmFlowCoordinatorTests
{
    private static MethodInfo RunSessionAsyncMethod { get; } =
        typeof(AlarmFlowCoordinator).GetMethod("RunSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("RunSessionAsync nicht gefunden - wurde die Methode umbenannt?");

    private static MethodInfo OnMyWayReceivedMethod { get; } =
        typeof(RepeatingAlarmSession).GetMethod("OnMyWayReceived", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("OnMyWayReceived nicht gefunden - wurde die Methode umbenannt?");

    [Fact]
    public async Task RunSessionAsync_PushesAuditLogAfterSessionEnds_WithoutBlockingOrThrowing()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();

        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var identity = new LiveIdentity(customerGroupId, deviceId, "Sender-PC", "Frau Test", "Zimmer", "1", Role.User, false, "0.0.0", 0);

        // Ein Admin-Peer mit einer bewusst nicht gerouteten Adresse (RFC 5737 TEST-NET-3) -
        // übt den echten Push-Versuch (Connect + AuditSyncRequestTimeout + internes Swallow
        // in AuditSyncService.SendPushAsync) aus, ohne Kollisionsrisiko mit einer eventuell
        // auf diesem Rechner echt laufenden Agent-Instanz auf demselben festen
        // AuditSyncTcpPort (AlarmFlowCoordinator kann diesen Port nicht überschreiben -
        // gleiches Risiko, das SendingTests.cs beim AlarmFeedbackTcpPort schon vermeidet).
        new DeviceStore().Save(new List<DeviceEntry>
        {
            new() { DeviceId = Guid.NewGuid(), Role = Role.Admin, IpAddress = "203.0.113.5" },
        });

        var feedbackChannel = new AlarmFeedbackChannel(() => identity);
        var auditLog = new AuditLog(() => deviceId);
        var auditSync = new AuditSyncService(() => identity, auditLog);
        var coordinator = new AlarmFlowCoordinator(
            () => identity, () => new OwnSettings { DeviceId = deviceId }, () => new SharedConfig(),
            feedbackChannel, auditLog, auditSync);

        var profile = new AlarmProfile { Text = "Testalarm", ResponseThreshold = 1 };
        var session = new RepeatingAlarmSession(profile, Array.Empty<DeviceEntry>(), identity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
        try
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Finished += (_, _) => finished.TrySetResult();

            var task = (Task)RunSessionAsyncMethod.Invoke(coordinator, new object?[] { session })!;

            // Schwellwert=1 sofort auslösen, damit die aktive Ping-Phase nicht die vollen
            // 5 Minuten läuft - übrig bleibt nur noch das unvermeidbare 1-Minuten-
            // Nachlauf-Fenster vor dem Audit-Push.
            var onMyWay = new AlarmOnMyWayMessage(
                customerGroupId, profile.Id, session.AlarmSessionId, Guid.NewGuid(),
                "Empf-PC", "Herr Novak", "Raum", DateTimeOffset.UtcNow);
            OnMyWayReceivedMethod.Invoke(session, new object?[] { feedbackChannel, onMyWay });

            // Der Alarmversand selbst darf nicht auf den Audit-Push warten: Finished feuert
            // in Sekunden, lange bevor der Gesamt-Task (inkl. Nachlauf + Push-Versuch) fertig ist.
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var exception = await Record.ExceptionAsync(() => task.WaitAsync(TimeSpan.FromSeconds(100)));
            Assert.Null(exception);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task RunSessionAsync_SwallowsAuditPushFailure_WithoutThrowing()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();

        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var identity = new LiveIdentity(customerGroupId, deviceId, "Sender-PC", "Frau Test", "Zimmer", "1", Role.User, false, "0.0.0", 0);

        // devices.json vor dem Aufruf kaputt schreiben: _deviceStore.Load() wird als
        // Argumentausdruck von _auditSync.PushPendingAsync(_deviceStore.Load()) innerhalb
        // des bestehenden try-Blocks in RunSessionAsync ausgewertet (AlarmFlowCoordinator.cs,
        // Kommentar "best-effort, blockiert nie den Alarm-Ablauf") - muss dort verschluckt
        // werden, nicht nach außen dringen.
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.DevicesFilePath)!);
        File.WriteAllText(AppPaths.DevicesFilePath, "{ kaputtes json");

        var feedbackChannel = new AlarmFeedbackChannel(() => identity);
        var auditLog = new AuditLog(() => deviceId);
        var auditSync = new AuditSyncService(() => identity, auditLog);
        var coordinator = new AlarmFlowCoordinator(
            () => identity, () => new OwnSettings { DeviceId = deviceId }, () => new SharedConfig(),
            feedbackChannel, auditLog, auditSync);

        var profile = new AlarmProfile { Text = "Testalarm", ResponseThreshold = 1 };
        var session = new RepeatingAlarmSession(profile, Array.Empty<DeviceEntry>(), identity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
        try
        {
            var task = (Task)RunSessionAsyncMethod.Invoke(coordinator, new object?[] { session })!;

            var onMyWay = new AlarmOnMyWayMessage(
                customerGroupId, profile.Id, session.AlarmSessionId, Guid.NewGuid(),
                "Empf-PC", "Herr Novak", "Raum", DateTimeOffset.UtcNow);
            OnMyWayReceivedMethod.Invoke(session, new object?[] { feedbackChannel, onMyWay });

            var exception = await Record.ExceptionAsync(() => task.WaitAsync(TimeSpan.FromSeconds(100)));
            Assert.Null(exception);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task SendSelfTestAsync_MarksOutgoingRequestAsTest()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();

        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var identity = new LiveIdentity(customerGroupId, deviceId, "Sender-PC", "Frau Test", "Zimmer", "1", Role.User, false, "0.0.0", 0);

        // Fehlerbericht "Sende-Bubble erscheint nicht" (18.08.2026): SendSelfTestAsync übergab
        // bislang kein isTest:true an die RepeatingAlarmSession - dadurch trug auch die per
        // Loopback ankommende AlarmRequestMessage.IsTest fälschlich false (AlarmPopupWindow hätte
        // beim Selbsttest keine TESTMODUS-Kennzeichnung gezeigt). Der Listener muss auf dem
        // echten, festen AlarmTcpPort mithören, weil SendSelfTestAsync das Self-Target nicht auf
        // einen freien Testport umleiten kann (AlarmFlowCoordinator.cs, AppConstants.AlarmTcpPort
        // fest verdrahtet) - gleiches akzeptiertes Kollisionsrisiko wie beim AuditSyncTcpPort im
        // Test oben in dieser Klasse.
        var listener = new AlarmTcpListener(() => identity);
        AlarmRequestMessage? received = null;
        var receivedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.AlarmReceived += (_, args) =>
        {
            received = args.Request;
            receivedSignal.TrySetResult();
        };
        listener.Start();

        try
        {
            var feedbackChannel = new AlarmFeedbackChannel(() => identity);
            var auditLog = new AuditLog(() => deviceId);
            var auditSync = new AuditSyncService(() => identity, auditLog);
            var coordinator = new AlarmFlowCoordinator(
                () => identity, () => new OwnSettings { DeviceId = deviceId }, () => new SharedConfig(),
                feedbackChannel, auditLog, auditSync);

            var profile = new AlarmProfile { Text = "Selbsttest", ResponseThreshold = 1 };

            await coordinator.SendSelfTestAsync(profile).WaitAsync(TimeSpan.FromSeconds(10));
            await receivedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(received);
            Assert.True(received!.IsTest);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }
}
