using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Sending;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Covers the FR-7 extension point (Pflichtenheft 5.9): still always sends immediately,
/// with no confirmation prompt, even after the Teil-2 rename to AlarmProfile.
/// </summary>
public class SendingTests
{
    [Fact]
    public async Task NoConfirmation_ReturnsTrue_WithoutAnyPrompt()
    {
        var hook = NoConfirmation.Instance;
        var profile = new AlarmProfile { Text = "Test" };
        var targets = new List<DeviceEntry>();

        var result = await hook.ConfirmAsync(profile, targets, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public void NoConfirmation_Instance_IsASingleton()
    {
        Assert.Same(NoConfirmation.Instance, NoConfirmation.Instance);
    }

    [Fact]
    public async Task RepeatingAlarmSession_StopsAfterProfilesConfiguredResponseThreshold()
    {
        // Regressionstest für den live gemeldeten Fehlerbericht 07.08.2026 ("Popup poppt
        // nach Schließen wieder auf, Schwellwert-Bedingung greift nicht"): RepeatingAlarmSession
        // prüfte bisher gegen den festen AppConstants.AlarmAutoStopResponseCount (=2, ein
        // Phase-1-Rest) statt gegen das pro Profil im Dashboard einstellbare
        // ResponseThreshold - bei Schwellwert 1 (Standard bei neuen Profilen) hörte der
        // Sender trotz einer einzigen "bin unterwegs"-Antwort nicht auf, alle 5s weiter zu
        // senden, obwohl der Empfänger seinen "Schließen"-Button (der ResponseThreshold
        // schon immer richtig benutzt) längst freigeschaltet bekam.
        //
        // Ruft OnMyWayReceived per Reflection direkt auf statt über einen echten TCP-
        // Roundtrip: AlarmFeedbackChannel.SendEnvelopeAsync verbindet für das Feedback
        // IMMER zu AppConstants.AlarmFeedbackTcpPort (nicht zu target.TcpPort - das ist ein
        // fester, protokollweiter Port, kein pro-Gerät-Feld, anders als beim Alarm-Kanal
        // selbst) - ein isolierter, portfreier Test hätte sonst mit einer bereits laufenden
        // echten Agent-Instanz auf diesem Rechner kollidieren können. Der eigentliche Bug
        // hier ist reine Vergleichslogik (Schwellwert), keine Netzwerk-Frage - dafür ist der
        // direkte Aufruf der richtige, nicht-brüchige Testansatz.
        var customerGroupId = Guid.NewGuid();
        var senderDeviceId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(customerGroupId, senderDeviceId, "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0, DateTimeOffset.UtcNow);

        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);
        var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = 1 };
        var session = new RepeatingAlarmSession(profile, new[] { UnreachableTarget() }, senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
        try
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Finished += (_, _) => finished.TrySetResult();
            var runTask = session.RunAsync();

            // Ein einziger "bin unterwegs" - bei ResponseThreshold=1 muss das allein schon
            // zum Stoppen reichen (statt der vorher hart verdrahteten 2).
            var onMyWay = new AlarmOnMyWayMessage(
                customerGroupId, profile.Id, session.AlarmSessionId, Guid.NewGuid(),
                "Empf-PC", "Herr Novak", "Raum", DateTimeOffset.UtcNow);

            var handler = typeof(RepeatingAlarmSession).GetMethod("OnMyWayReceived", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("OnMyWayReceived nicht gefunden - wurde die Methode umbenannt?");
            handler.Invoke(session, new object?[] { feedbackChannel, onMyWay });

            // Deutlich unter AppConstants.AlarmMaxDuration (5 Minuten) - die Session muss
            // durch die Schwellwert-Antwort stoppen, nicht durch den Hard-Timeout.
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // runTask absichtlich NICHT auf Abschluss abwarten (09.08.2026): RunAsync läuft
            // nach Finished noch bis zu AppConstants.AlarmAutoCloseAfterLastSignal (1 Minute)
            // weiter, um Nachzügler-"bin unterwegs"-Antworten noch als Info-Update zu
            // relayen (siehe Klassendoku) - genau das war der hier gemeldete Fehler
            // ("Nachzügler-Antworten verschwinden spurlos"). Finished bleibt der richtige,
            // schnelle Nachweis dafür, dass das eigentliche PINGEN sofort gestoppt hat.
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task RepeatingAlarmSession_RelaysLateOnMyWayAfterThreshold_TaggedAsNoLongerSending()
    {
        // Regressionstest für den Fehlerbericht vom 09.08.2026 ("Nachzügler-Antworten nach
        // Schwellwert verschwinden spurlos, andere Geräte erfahren nichts davon"):
        // RepeatingAlarmSession kappte bisher sofort nach dem Schwellwert-Stopp (bzw. jedem
        // anderen Stopp-Grund) über Dispose() die OnMyWayReceived-Subscription - eine "bin
        // unterwegs"-Antwort, die auch nur eine Millisekunde später ankam, ging komplett
        // verloren: kein Relay an die übrigen Geräte, keine Aktualisierung der eigenen
        // Statusanzeige. Die Anforderung ist explizit die Ausnahme: solche Spätantworten
        // sollen weiterhin als Anzeige-Update relayt werden (Namensliste bleibt korrekt),
        // nur eben mit SenderStillSending=false markiert statt als neue Ping-Welle zu zählen.
        var customerGroupId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(customerGroupId, Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0, DateTimeOffset.UtcNow);

        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);
        var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = 1 };
        var session = new RepeatingAlarmSession(profile, new[] { UnreachableTarget() }, senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
        try
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Finished += (_, _) => finished.TrySetResult();

            var statusUpdates = new List<AlarmSessionStatus>();
            session.StatusChanged += (_, status) => statusUpdates.Add(status);

            _ = session.RunAsync();

            var handler = typeof(RepeatingAlarmSession).GetMethod("OnMyWayReceived", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("OnMyWayReceived nicht gefunden - wurde die Methode umbenannt?");

            // Erste Antwort erreicht den Schwellwert (=1) und stoppt das Pingen.
            var firstResponder = new AlarmOnMyWayMessage(
                customerGroupId, profile.Id, session.AlarmSessionId, Guid.NewGuid(),
                "Empf-PC-1", "Herr Novak", "Raum 1", DateTimeOffset.UtcNow);
            handler.Invoke(session, new object?[] { feedbackChannel, firstResponder });
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

            statusUpdates.Clear();

            // Zweite Antwort trifft ERST NACH Finished ein (Nachzügler) - muss trotzdem
            // relayt werden, nur eben nicht mehr als "es wird noch gepingt".
            var lateResponder = new AlarmOnMyWayMessage(
                customerGroupId, profile.Id, session.AlarmSessionId, Guid.NewGuid(),
                "Empf-PC-2", "Herr Baumann", "Raum 2", DateTimeOffset.UtcNow);
            handler.Invoke(session, new object?[] { feedbackChannel, lateResponder });

            // Fire-and-forget-Relay in OnMyWayReceived - kurz auf den StatusChanged-Event warten.
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            var lateUpdate = Assert.Single(statusUpdates);
            Assert.False(lateUpdate.StillSending); // kein Hilferuf mehr - nur Anzeige-Update
            Assert.Equal(2, lateUpdate.OnTheWayNames.Count); // Nachzügler taucht in der Liste auf, geht nicht verloren
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task RepeatingAlarmSession_ThresholdTwo_KeepsSendingAfterFirstResponse_AndStopsAfterSecond()
    {
        var (session, feedbackChannel, customerGroupId) = NewSession(responseThreshold: 2);
        try
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Finished += (_, _) => finished.TrySetResult();
            var statusUpdates = new System.Collections.Concurrent.ConcurrentQueue<AlarmSessionStatus>();
            session.StatusChanged += (_, status) => statusUpdates.Enqueue(status);
            _ = session.RunAsync();

            var first = Guid.NewGuid();
            InvokeOnMyWay(session, feedbackChannel, OnMyWay(customerGroupId, session, first));
            // Dasselbe Gerät ein zweites Mal (z. B. doppelt zugestellt) zählt nicht als zweite Person.
            InvokeOnMyWay(session, feedbackChannel, OnMyWay(customerGroupId, session, first));
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            Assert.False(finished.Task.IsCompleted, "Die Session stoppt bei Schwelle 2 schon nach einer Rückmeldung.");
            Assert.All(statusUpdates, s => Assert.True(s.StillSending));
            Assert.Contains(statusUpdates, s => s.OnTheWayNames.Count == 1);
            Assert.DoesNotContain(statusUpdates, s => s.OnTheWayNames.Count > 1);

            InvokeOnMyWay(session, feedbackChannel, OnMyWay(customerGroupId, session, Guid.NewGuid()));
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(AlarmStopReason.ThresholdReached, session.StopReason);
            Assert.Contains(statusUpdates, s => !s.StillSending && s.OnTheWayNames.Count == 2);
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task RepeatingAlarmSession_Cancel_SendsFinalStatusAsNoLongerSending()
    {
        // Abbruch an alle: das letzte Relay mit SenderStillSending=false gibt jedes Empfängerfenster frei (AlarmCloseGate).
        var (session, _, _) = NewSession(responseThreshold: 2);
        try
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Finished += (_, _) => finished.TrySetResult();
            var statusUpdates = new System.Collections.Concurrent.ConcurrentQueue<AlarmSessionStatus>();
            session.StatusChanged += (_, status) => statusUpdates.Enqueue(status);
            _ = session.RunAsync();

            session.Cancel();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(AlarmStopReason.Cancelled, session.StopReason);
            Assert.False(statusUpdates.Last().StillSending);
        }
        finally
        {
            session.Dispose();
        }
    }

    private static (RepeatingAlarmSession Session, AlarmFeedbackChannel FeedbackChannel, Guid CustomerGroupId) NewSession(int responseThreshold)
    {
        var customerGroupId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(customerGroupId, Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0, DateTimeOffset.UtcNow);
        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);
        var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = responseThreshold };
        var session = new RepeatingAlarmSession(profile, new[] { UnreachableTarget() }, senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
        return (session, feedbackChannel, customerGroupId);
    }

    // Ohne Ziel endet eine Sitzung nach einer Welle; ein Ziel ohne IP hält sie am Laufen,
    // ohne dass ein Sendeversuch das Netz berührt.
    private static DeviceEntry UnreachableTarget() => new() { DeviceId = Guid.NewGuid(), IpAddress = string.Empty };

    private static AlarmOnMyWayMessage OnMyWay(Guid customerGroupId, RepeatingAlarmSession session, Guid responderDeviceId) =>
        new(customerGroupId, session.Profile.Id, session.AlarmSessionId, responderDeviceId, "Empf-PC", "Herr Novak", "Raum", DateTimeOffset.UtcNow);

    // Per Reflection statt über TCP, Begründung im ersten RepeatingAlarmSession-Test oben.
    private static void InvokeOnMyWay(RepeatingAlarmSession session, AlarmFeedbackChannel feedbackChannel, AlarmOnMyWayMessage message)
    {
        var handler = typeof(RepeatingAlarmSession).GetMethod("OnMyWayReceived", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("OnMyWayReceived nicht gefunden - wurde die Methode umbenannt?");
        handler.Invoke(session, new object?[] { feedbackChannel, message });
    }

    [Fact]
    public async Task RepeatingAlarmSession_LateAckInLaterRound_KeepsEarlierReceipt()
    {
        // Runde 1 wird sofort quittiert, ab Runde 2 kommt die Quittung erst nach
        // AlarmAckTimeout - der Empfänger muss trotzdem als empfangen gezählt bleiben.
        var customerGroupId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(customerGroupId, Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0, DateTimeOffset.UtcNow);
        var receiverDeviceId = Guid.NewGuid();

        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        using var serverCts = new CancellationTokenSource();
        var serverTask = RunDelayingAckServerAsync(tcp, customerGroupId, receiverDeviceId, serverCts.Token);

        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);
        var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = 1 };
        var target = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = port };
        var session = new RepeatingAlarmSession(profile, new[] { target }, senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
        try
        {
            var sendingUpdates = new List<AlarmSessionStatus>();
            var secondRound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.StatusChanged += (_, status) =>
            {
                if (!status.StillSending)
                {
                    return;
                }

                lock (sendingUpdates)
                {
                    sendingUpdates.Add(status);
                    if (sendingUpdates.Count >= 2)
                    {
                        secondRound.TrySetResult();
                    }
                }
            };

            _ = session.RunAsync();
            await secondRound.Task.WaitAsync(TimeSpan.FromSeconds(30));

            lock (sendingUpdates)
            {
                Assert.Equal(1, sendingUpdates[0].AckedCount);
                Assert.Equal(1, sendingUpdates[1].AckedCount);
            }
        }
        finally
        {
            session.Cancel();
            session.Dispose();
            serverCts.Cancel();
            tcp.Stop();
            await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(2)));
        }
    }

    [Theory]
    [InlineData(0, 5000)]
    [InlineData(2000, 3000)]
    [InlineData(5000, 0)]
    [InlineData(9000, 0)]
    public void TimeUntilNextWave_CountsFromWaveStart(int waveDurationMs, int expectedMs)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), RepeatingAlarmSession.TimeUntilNextWave(TimeSpan.FromMilliseconds(waveDurationMs)));
    }

    [Theory]
    [InlineData(2000)] // Empfänger quittiert spät, z. B. weil sein UI-Thread beim ersten Alarm noch beschäftigt ist
    [InlineData(6000)] // Empfänger quittiert gar nicht mehr (Agent beendet, PC aus): jede Welle läuft in den Ack-Timeout
    public async Task RepeatingAlarmSession_SlowAck_KeepsRepeatInterval(int ackDelayMs)
    {
        // Ein langsamer Empfänger darf den Takt der Wiederholung für alle nicht verlängern.
        var ackDelay = TimeSpan.FromMilliseconds(ackDelayMs);
        var tolerance = TimeSpan.FromSeconds(1);
        var customerGroupId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(customerGroupId, Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0, DateTimeOffset.UtcNow);
        var receiverDeviceId = Guid.NewGuid();

        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        using var serverCts = new CancellationTokenSource();
        var waveArrivals = new System.Collections.Concurrent.ConcurrentQueue<DateTimeOffset>();
        var serverTask = RunDelayingAckServerAsync(tcp, customerGroupId, receiverDeviceId, _ =>
        {
            waveArrivals.Enqueue(DateTimeOffset.UtcNow);
            return ackDelay;
        }, serverCts.Token);

        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);
        var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = 1 };
        var target = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = port };
        var session = new RepeatingAlarmSession(profile, new[] { target }, senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
        try
        {
            _ = session.RunAsync();
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
            while (waveArrivals.Count < 3 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            var arrivals = waveArrivals.ToArray();
            Assert.True(arrivals.Length >= 3, $"Nur {arrivals.Length} Wellen in 30 s angekommen.");
            for (var i = 1; i < 3; i++)
            {
                var gap = arrivals[i] - arrivals[i - 1];
                Assert.True(gap <= AppConstants.AlarmRepeatInterval + tolerance,
                    $"Abstand Welle {i} -> {i + 1}: {gap.TotalSeconds:F2} s, erwartet höchstens {(AppConstants.AlarmRepeatInterval + tolerance).TotalSeconds:F1} s.");
            }
        }
        finally
        {
            session.Cancel();
            session.Dispose();
            serverCts.Cancel();
            tcp.Stop();
            await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public async Task RepeatingAlarmSession_OnMyWayWithoutAck_CountsAsReceived()
    {
        var customerGroupId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(customerGroupId, Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0, DateTimeOffset.UtcNow);
        var receiverDeviceId = Guid.NewGuid();

        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);
        var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = 1 };
        // Ohne gültige IP schlägt jeder Sendeversuch sofort fehl - es kommt nie eine Quittung.
        var target = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = string.Empty };
        var session = new RepeatingAlarmSession(profile, new[] { target }, senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
        try
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Finished += (_, _) => finished.TrySetResult();
            var updates = new List<AlarmSessionStatus>();
            session.StatusChanged += (_, status) =>
            {
                lock (updates)
                {
                    updates.Add(status);
                }
            };

            _ = session.RunAsync();

            var handler = typeof(RepeatingAlarmSession).GetMethod("OnMyWayReceived", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("OnMyWayReceived nicht gefunden - wurde die Methode umbenannt?");
            var onMyWay = new AlarmOnMyWayMessage(
                customerGroupId, profile.Id, session.AlarmSessionId, receiverDeviceId,
                "Empf-PC", "Herr Novak", "Raum", DateTimeOffset.UtcNow);
            handler.Invoke(session, new object?[] { feedbackChannel, onMyWay });

            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));

            lock (updates)
            {
                Assert.Equal(1, updates[^1].AckedCount);
                Assert.Equal(1, updates[^1].TargetCount);
            }
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task RepeatingAlarmSession_WithoutTargets_SendsOneWaveAndStops()
    {
        // Ein Alarm ohne Empfänger geht genau einmal raus und endet sofort,
        // statt minutenlang ins Leere zu wiederholen.
        var auditLines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var senderIdentity = new LiveIdentity(Guid.NewGuid(), Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.Admin, false, "0.0.0", 0, DateTimeOffset.UtcNow);
        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);
        var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = 1 };
        var session = new RepeatingAlarmSession(profile, Array.Empty<DeviceEntry>(), senderIdentity, new AlarmSender(auditLines.Enqueue), feedbackChannel, DateTimeOffset.UtcNow);
        try
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.Finished += (_, _) => finished.TrySetResult();
            var statusUpdates = new System.Collections.Concurrent.ConcurrentQueue<AlarmSessionStatus>();
            session.StatusChanged += (_, status) => statusUpdates.Enqueue(status);
            var runTask = session.RunAsync();

            await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(AlarmStopReason.NoRecipients, session.StopReason);
            Assert.Contains(statusUpdates, s => !s.StillSending && s.TargetCount == 0 && s.AckedCount == 0);

            // Ohne Empfänger gibt es auch keine Spätantworten, auf die die Sitzung noch warten müsste.
            await runTask.WaitAsync(TimeSpan.FromSeconds(3));

            // Länger als ein Wiederholungstakt: Es darf keine zweite Welle folgen.
            await Task.Delay(AppConstants.AlarmRepeatInterval + TimeSpan.FromSeconds(1));
            Assert.Single(auditLines, l => l.Contains($"alarm sent alarmProfileId={profile.Id}", StringComparison.Ordinal) && l.EndsWith("targetCount=0", StringComparison.Ordinal));
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public async Task RepeatingAlarmSession_TargetAddressChangedAfterTrigger_WaveUsesCurrentAddress()
    {
        // Das Ziel startet gerade und ist unter der beim Auslösen gespeicherten Adresse
        // nicht erreichbar; sein Boot-Call hat dem Sender inzwischen die aktuelle Adresse
        // mitgeteilt. Die Session muss das Ziel unter der aktuellen Adresse anschreiben,
        // statt bis zum Ende der Session die alte zu verwenden.
        var customerGroupId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(customerGroupId, Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0, DateTimeOffset.UtcNow);
        var receiverDeviceId = Guid.NewGuid();
        var receiverIdentity = new LiveIdentity(customerGroupId, receiverDeviceId, "Empf-PC", "Herr Novak", "Raum", "2", Role.Admin, false, "0.0.0", 0, DateTimeOffset.UtcNow);

        var stalePort = FreeTcpPort();
        var currentPort = FreeTcpPort();
        await using var receiver = new AlarmTcpListener(() => receiverIdentity);
        Assert.True(receiver.Start(currentPort));

        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);
        var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = 1 };
        var snapshotTarget = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = stalePort };
        var storedDevices = new List<DeviceEntry>
        {
            new() { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = currentPort },
        };
        var session = new RepeatingAlarmSession(
            profile, new[] { snapshotTarget }, senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow,
            currentDevicesProvider: () => storedDevices);
        try
        {
            var acked = new TaskCompletionSource<AlarmSessionStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.StatusChanged += (_, status) =>
            {
                if (status.StillSending && status.AckedCount > 0)
                {
                    acked.TrySetResult(status);
                }
            };

            _ = session.RunAsync();
            var status = await acked.Task.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.Equal(1, status.TargetCount);
            Assert.Equal(1, status.AckedCount);
        }
        finally
        {
            session.Cancel();
            session.Dispose();
        }
    }

    [Fact]
    public void SenderStatusTexts_Headline_NoRecipientsFromTheStart()
    {
        // Ohne Empfänger steht die Abbruchmeldung sofort da, nie "Alarm wird gesendet...".
        Assert.Equal("Kein Empfänger eingestellt!", SenderStatusTexts.Headline(stillSending: true, targetCount: 0));
        Assert.Equal("Kein Empfänger eingestellt!", SenderStatusTexts.Headline(stillSending: false, targetCount: 0));
        Assert.Equal("Alarm wird gesendet...", SenderStatusTexts.Headline(stillSending: true, targetCount: 2));
        Assert.Equal("Alarm beendet", SenderStatusTexts.Headline(stillSending: false, targetCount: 2));
    }

    [Fact]
    public void SenderStatusTexts_NoRecipientsHint_NamesWhereToFixIt()
    {
        Assert.Equal("Info: Ein Admin muss im Dashboard Empfänger für dieses Profil einrichten.", SenderStatusTexts.NoRecipientsHint(targetCount: 0));
    }

    [Fact]
    public void SenderStatusTexts_NoRecipientsHint_IsNullWithRecipients()
    {
        Assert.Null(SenderStatusTexts.NoRecipientsHint(targetCount: 1));
    }

    [Fact]
    public void SenderStatusTexts_MaintenanceHint_IsNeutral()
    {
        Assert.Equal("Wartung erforderlich", SenderStatusTexts.MaintenanceHintTitle);
        Assert.Equal("Ein Hintergrunddienst konnte eine Meldung nicht zustellen (Code H-00). Bitte die IT informieren.", SenderStatusTexts.MaintenanceHintText);
        foreach (var text in new[] { SenderStatusTexts.MaintenanceHintTitle, SenderStatusTexts.MaintenanceHintText })
        {
            Assert.DoesNotContain("HälpMi", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HaelpMi", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Alarm", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AppConstants_NoRecipientsTimings_MatchMockup()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), AppConstants.NoRecipientsMaintenanceHintDelay);
        Assert.InRange(AppConstants.SenderStatusNoRecipientsAutoClose, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MaintenanceHintScheduler_RaisesOnceAfterTheDelay()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeSpan? requested = null;
        var scheduler = new MaintenanceHintScheduler(TimeSpan.FromSeconds(30), delay =>
        {
            requested = delay;
            return gate.Task;
        });
        var raised = 0;
        scheduler.Due += (_, _) => Interlocked.Increment(ref raised);

        var first = scheduler.Schedule();
        Assert.Equal(TimeSpan.FromSeconds(30), requested);
        Assert.Equal(0, raised);

        gate.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task MaintenanceHintScheduler_RepeatedTriggersWhileWaiting_GiveOneHint()
    {
        // Mehrmals das Kürzel gedrückt: trotzdem nur ein Hinweis.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waits = 0;
        var scheduler = new MaintenanceHintScheduler(TimeSpan.FromSeconds(30), _ =>
        {
            Interlocked.Increment(ref waits);
            return gate.Task;
        });
        var raised = 0;
        scheduler.Due += (_, _) => Interlocked.Increment(ref raised);

        var first = scheduler.Schedule();
        var second = scheduler.Schedule();
        Assert.True(second.IsCompleted);
        gate.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, waits);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task MaintenanceHintScheduler_AfterAHint_CanScheduleAgain()
    {
        var scheduler = new MaintenanceHintScheduler(TimeSpan.FromSeconds(30), _ => Task.CompletedTask);
        var raised = 0;
        scheduler.Due += (_, _) => Interlocked.Increment(ref raised);

        await scheduler.Schedule().WaitAsync(TimeSpan.FromSeconds(3));
        await scheduler.Schedule().WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, raised);
    }

    [Fact]
    public void MaintenanceHintTexts_FitTheNotificationLimits()
    {
        // HaelpMi.Notify lehnt längere Argumente ab (NOTIFYICONDATA: 63 bzw. 255 Zeichen).
        Assert.InRange(SenderStatusTexts.MaintenanceHintTitle.Length, 1, 63);
        Assert.InRange(SenderStatusTexts.MaintenanceHintText.Length, 1, 255);
    }

    [Fact]
    public void MaintenanceHintLauncher_StartsNotifyNextToTheAgentWithTitleAndText()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "agent");
        ProcessStartInfo? gestartet = null;
        var warnungen = new List<string>();
        var launcher = new MaintenanceHintLauncher(ordner, info =>
        {
            gestartet = info;
            return Process.GetCurrentProcess();
        }, (meldung, _) => warnungen.Add(meldung));

        Assert.True(launcher.Launch());

        Assert.NotNull(gestartet);
        Assert.Equal(Path.Combine(ordner, "HaelpMi.Notify.exe"), gestartet!.FileName);
        Assert.Equal(ordner, gestartet.WorkingDirectory);
        Assert.False(gestartet.UseShellExecute);
        Assert.Equal(new[] { SenderStatusTexts.MaintenanceHintTitle, SenderStatusTexts.MaintenanceHintText }, gestartet.ArgumentList);
        Assert.Empty(warnungen);
    }

    [Fact]
    public void MaintenanceHintLauncher_MissingExe_WarnsAndDoesNotThrow()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "HaelpMiNotifyFehlt_" + Guid.NewGuid().ToString("N"));
        var warnungen = new List<(string Meldung, Exception? Ausnahme)>();
        var launcher = new MaintenanceHintLauncher(ordner, warn: (meldung, ausnahme) => warnungen.Add((meldung, ausnahme)));

        Assert.False(launcher.Launch());

        var warnung = Assert.Single(warnungen);
        Assert.Contains("HaelpMi.Notify.exe", warnung.Meldung);
        Assert.Contains("neu installieren", warnung.Meldung);
        Assert.NotNull(warnung.Ausnahme);
    }

    [Fact]
    public void MaintenanceHintLauncher_ProcessNotStarted_Warns()
    {
        var warnungen = new List<string>();
        var launcher = new MaintenanceHintLauncher(Path.GetTempPath(), _ => null, (meldung, _) => warnungen.Add(meldung));

        Assert.False(launcher.Launch());
        Assert.Single(warnungen);
    }

    [Fact]
    public async Task MaintenanceHint_RepeatedTriggers_StartNotifyOnceAfterTheDelay()
    {
        // Zusammenspiel wie im Agent: Drosselung, danach genau ein Start des Hilfsprogramms.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new MaintenanceHintScheduler(AppConstants.NoRecipientsMaintenanceHintDelay, _ => gate.Task);
        var starts = 0;
        var launcher = new MaintenanceHintLauncher(Path.GetTempPath(), _ =>
        {
            Interlocked.Increment(ref starts);
            return Process.GetCurrentProcess();
        }, (_, _) => { });
        scheduler.Due += (_, _) => launcher.Launch();

        var first = scheduler.Schedule();
        _ = scheduler.Schedule();
        _ = scheduler.Schedule();
        Assert.Equal(0, starts);

        gate.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task RepeatingAlarmSession_NoCurrentAddress_KeepsSnapshotAddress()
    {
        // Ein Ziel ohne Adresse in devices.json wird weiter unter der Adresse aus dem
        // Auslösezeitpunkt angeschrieben; ebenso ein Ziel, das dort gar nicht mehr steht.
        var customerGroupId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(customerGroupId, Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0, DateTimeOffset.UtcNow);
        var receiverDeviceId = Guid.NewGuid();
        var receiverIdentity = new LiveIdentity(customerGroupId, receiverDeviceId, "Empf-PC", "Herr Novak", "Raum", "2", Role.User, false, "0.0.0", 0, DateTimeOffset.UtcNow);

        var port = FreeTcpPort();
        await using var receiver = new AlarmTcpListener(() => receiverIdentity);
        Assert.True(receiver.Start(port));

        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);
        var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = 1 };
        var snapshotTarget = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = port };
        var storedDevices = new List<DeviceEntry>
        {
            new() { DeviceId = receiverDeviceId, IpAddress = string.Empty, TcpPort = FreeTcpPort() },
        };
        var missingDeviceId = Guid.NewGuid();
        var missingTarget = new DeviceEntry { DeviceId = missingDeviceId, IpAddress = "127.0.0.1", TcpPort = port };
        var session = new RepeatingAlarmSession(
            profile, new[] { snapshotTarget, missingTarget }, senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow,
            currentDevicesProvider: () => storedDevices);
        try
        {
            // Erst ab der zweiten Welle werden die Adressen nachgelesen: zwei Ziele mal zwei Wellen.
            var deliveries = 0;
            var secondWaveDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            receiver.AlarmReceived += (_, _) =>
            {
                if (Interlocked.Increment(ref deliveries) >= 4)
                {
                    secondWaveDelivered.TrySetResult();
                }
            };

            _ = session.RunAsync();
            await secondWaveDelivered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            session.Cancel();
            session.Dispose();
        }
    }

    private static int FreeTcpPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static Task RunDelayingAckServerAsync(TcpListener tcp, Guid customerGroupId, Guid receiverDeviceId, CancellationToken ct) =>
        RunDelayingAckServerAsync(tcp, customerGroupId, receiverDeviceId,
            round => round == 1 ? TimeSpan.Zero : AppConstants.AlarmAckTimeout + TimeSpan.FromSeconds(1), ct);

    private static async Task RunDelayingAckServerAsync(TcpListener tcp, Guid customerGroupId, Guid receiverDeviceId, Func<int, TimeSpan> ackDelayForRound, CancellationToken ct)
    {
        var round = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await tcp.AcceptTcpClientAsync(ct);
                round++;
                var delay = ackDelayForRound(round);
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try
                        {
                            var stream = client.GetStream();
                            using var reader = new StreamReader(stream, NetworkSerializer.Encoding, leaveOpen: true);
                            var line = await reader.ReadLineAsync(ct);
                            var request = line is null ? null : NetworkSerializer.FromJsonLine<AlarmRequestMessage>(line);
                            if (request is null)
                            {
                                return;
                            }

                            await Task.Delay(delay, ct);
                            var ack = new AlarmAckMessage(customerGroupId, request.AlarmProfileId, request.AlarmSessionId, receiverDeviceId, DateTimeOffset.UtcNow);
                            var payload = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(ack));
                            await stream.WriteAsync(payload, ct);
                        }
                        catch (Exception)
                        {
                            // Sender hat nach dem Timeout bereits geschlossen
                        }
                    }
                }, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Listener gestoppt
        }
    }
}
