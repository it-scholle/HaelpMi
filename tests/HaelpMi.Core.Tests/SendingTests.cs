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
            await runTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            session.Dispose();
        }
    }
}
