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
        var session = new RepeatingAlarmSession(profile, Array.Empty<DeviceEntry>(), senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
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
        var session = new RepeatingAlarmSession(profile, Array.Empty<DeviceEntry>(), senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
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
        var session = new RepeatingAlarmSession(profile, Array.Empty<DeviceEntry>(), senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
        return (session, feedbackChannel, customerGroupId);
    }

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
