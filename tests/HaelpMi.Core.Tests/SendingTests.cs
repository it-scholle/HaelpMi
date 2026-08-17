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
        var senderIdentity = new LiveIdentity(customerGroupId, senderDeviceId, "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0);

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
        var senderIdentity = new LiveIdentity(customerGroupId, Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0);

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
    public async Task RepeatingAlarmSession_Cancel_ReturnsQuickly_EvenWhileSendIsStillPendingAgainstAnUnresponsiveTarget()
    {
        // Regressionstest für den Fehlerbericht "UI friert beim Abbrechen ein" (17.08.2026,
        // siehe SenderStatusWindow.CancelButton_Click): Cancel() ruft _stopCts.Cancel() auf,
        // das alle verlinkten Abbruch-Callbacks (Socket-Teardown der noch offenen Ziel-Sends,
        // siehe AlarmSender.SendToOneAsync) synchron auf dem aufrufenden Thread abarbeitet.
        // Dieser Test hält absichtlich eine echte TCP-Verbindung offen (Listener nimmt an,
        // antwortet nie), damit AlarmSender.SendAsync beim Cancel()-Aufruf garantiert noch
        // mitten in einer wartenden Netzwerkoperation steckt - genau der im Screenshot
        // dokumentierte Zustand ("Alarm wird gesendet... Empfangen: 0 von 2"). Cancel() selbst
        // muss trotzdem sofort zurückkehren, nicht erst nach AlarmAckTimeout (5s) oder gar
        // unbegrenzt.
        var customerGroupId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(customerGroupId, Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0);
        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            // Verbindung annehmen, aber nie etwas zurückschreiben und nie schließen -
            // simuliert die "träge/schwer erreichbare Gegenstelle" aus der Root-Cause-Hypothese.
            var acceptTask = listener.AcceptTcpClientAsync();

            var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = 1 };
            var target = new DeviceEntry { DeviceId = Guid.NewGuid(), IpAddress = "127.0.0.1", TcpPort = port };
            var session = new RepeatingAlarmSession(profile, new[] { target }, senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
            try
            {
                // Finished statt runTask abwarten (wie in den beiden Tests oben): RunAsync
                // läuft nach dem Stoppen noch bis zu AlarmAutoCloseAfterLastSignal (1 Minute)
                // weiter (Nachlauf-Fenster für Spätantworten, siehe Klassendoku) - das ist
                // hier nicht der Punkt, es geht nur darum, dass das PINGEN sofort stoppt.
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                session.Finished += (_, _) => finished.TrySetResult();
                _ = session.RunAsync();

                // Sicherstellen, dass der Sende-Versuch tatsächlich schon in der wartenden
                // Netzwerkoperation hängt, bevor abgebrochen wird.
                using var acceptedClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));

                var stopwatch = Stopwatch.StartNew();
                session.Cancel();
                stopwatch.Stop();

                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1),
                    $"Cancel() blockierte {stopwatch.Elapsed.TotalMilliseconds}ms - genau das UI-Freeze-Symptom aus dem Fehlerbericht.");

                await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                session.Dispose();
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RepeatingAlarmSession_FastOnMyWayResponse_DoesNotDiscardStillPendingRealAck()
    {
        // Regressionstest für den Fehlerbericht "Empfangen 0 von N bleibt dauerhaft hängen"
        // (Flaw 6, 17.08.2026): RunAsync übergab bisher denselben _stopCts.Token sowohl an
        // die Wellen-/Delay-Schleife als auch an AlarmSender.SendAsync für die Ack-Wartephase
        // DERSELBEN, noch laufenden Welle. Erreichte eine schnelle "Ich komme"-Antwort
        // (Standard-Schwellwert 1) den Auto-Stop, bevor die noch offene Ack-Wartephase eines
        // Ziels zurück war, brach das dieselbe ab - das Ziel zählte fälschlich als "nicht
        // empfangen", obwohl es (siehe die "Ich komme"-Antwort selbst) den Alarm nachweislich
        // bekommen hatte. Dieser Test hält einen echten TCP-Listener, der die Anfrage liest,
        // absichtlich ~300ms wartet (simuliert Ack-Verzögerung/Jitter) und DANN einen gültigen
        // Ack zurückschreibt - währenddessen löst eine per Reflection direkt aufgerufene
        // OnMyWayReceived (gleiche Technik wie im Schwellwert-Test oben) den Auto-Stop aus,
        // bevor der Ack zurück ist. Ohne den Fix (RunAsync/AlarmSender.SendAsync bekommen
        // einen eigenen, vom Schwellwert-Auto-Stop unberührten Token) zählt AckedCount trotz
        // erfolgreicher Zustellung dauerhaft 0.
        var customerGroupId = Guid.NewGuid();
        var senderIdentity = new LiveIdentity(customerGroupId, Guid.NewGuid(), "Sender-PC", "Frau Meier", "Zimmer", "1", Role.User, false, "0.0.0", 0);
        var feedbackChannel = new AlarmFeedbackChannel(() => senderIdentity);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var profile = new AlarmProfile { Text = "Bitte kommen!", ResponseThreshold = 1 };
            var target = new DeviceEntry { DeviceId = Guid.NewGuid(), IpAddress = "127.0.0.1", TcpPort = port };
            var session = new RepeatingAlarmSession(profile, new[] { target }, senderIdentity, new AlarmSender(), feedbackChannel, DateTimeOffset.UtcNow);
            try
            {
                var statusUpdates = new List<AlarmSessionStatus>();
                session.StatusChanged += (_, status) => statusUpdates.Add(status);
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                session.Finished += (_, _) => finished.TrySetResult();

                // runTask absichtlich nicht bis zum Abschluss abwarten (gleiches Muster wie in
                // den beiden Tests oben): RunAsync läuft nach Finished noch bis zu
                // AppConstants.AlarmAutoCloseAfterLastSignal (1 Minute) weiter - Finished ist
                // der richtige, schnelle Nachweis, dass das eigentliche Pingen fertig ist.
                _ = session.RunAsync();

                using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await using var serverStream = client.GetStream();
                var requestLine = await BoundedLineReader.ReadLineAsync(serverStream, CancellationToken.None)
                    ?? throw new InvalidOperationException("keine Anfrage empfangen");
                var request = NetworkSerializer.FromJsonLine<AlarmRequestMessage>(requestLine)
                    ?? throw new InvalidOperationException("Anfrage nicht deserialisierbar");

                // "Ich komme" trifft ein, WÄHREND der Ack noch aussteht (siehe Verzögerung
                // unten) - genau das Race aus dem Fehlerbericht.
                var onMyWay = new AlarmOnMyWayMessage(
                    customerGroupId, profile.Id, session.AlarmSessionId, Guid.NewGuid(),
                    "Empf-PC", "Herr Novak", "Raum", DateTimeOffset.UtcNow);
                var handler = typeof(RepeatingAlarmSession).GetMethod("OnMyWayReceived", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("OnMyWayReceived nicht gefunden - wurde die Methode umbenannt?");
                handler.Invoke(session, new object?[] { feedbackChannel, onMyWay });

                // Simuliert Ack-Verzögerung/Jitter: deutlich unter AlarmAckTimeout (5s), aber
                // lang genug, dass der Schwellwert-Auto-Stop oben garantiert zuerst feuert.
                await Task.Delay(TimeSpan.FromMilliseconds(300));

                var ack = new AlarmAckMessage(customerGroupId, request.AlarmProfileId, request.AlarmSessionId, target.DeviceId, DateTimeOffset.UtcNow);
                var ackBytes = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(ack));
                await serverStream.WriteAsync(ackBytes);
                await serverStream.FlushAsync();

                await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.NotEmpty(statusUpdates);
                Assert.Equal(1, statusUpdates[^1].AckedCount);
            }
            finally
            {
                session.Dispose();
            }
        }
        finally
        {
            listener.Stop();
        }
    }
}
