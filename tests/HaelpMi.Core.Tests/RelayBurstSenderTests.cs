using HaelpMi.Core.Sending;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Issue #136 Fixvorschlag 5: das abschließende "Sender pingt nicht mehr"-Relay ist
/// Best-Effort (<c>AlarmFeedbackChannel.SendEnvelopeAsync</c> schluckt einzelne
/// Fehlschläge) - ein einzelnes verlorenes Paket ließ den Empfänger-"Schließen"-Button nie
/// garantiert frei. Diese Tests decken nur die netzwerkfreie Wiederholungs-Schleife selbst
/// ab (Anzahl/Abstand), nicht den eigentlichen TCP-Versand - siehe RelayBurstSender-Klassenkommentar.
/// </summary>
public class RelayBurstSenderTests
{
    [Fact]
    public async Task SendRemainingAsync_CallsSendOnce_TotalCountMinusOneTimes()
    {
        var callCount = 0;

        await RelayBurstSender.SendRemainingAsync(
            () => { callCount++; return Task.CompletedTask; },
            totalCount: 5,
            spacing: TimeSpan.FromMilliseconds(1));

        Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task SendRemainingAsync_DoesNotCallSendOnce_WhenTotalCountIsOne()
    {
        // totalCount=1 heißt "nur das bereits vorher gesendete erste Exemplar" - keine
        // zusätzliche Wiederholung.
        var callCount = 0;

        await RelayBurstSender.SendRemainingAsync(
            () => { callCount++; return Task.CompletedTask; },
            totalCount: 1,
            spacing: TimeSpan.FromMilliseconds(1));

        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task SendRemainingAsync_WaitsSpacingBeforeEachCall()
    {
        var timestamps = new List<DateTime>();

        await RelayBurstSender.SendRemainingAsync(
            () => { timestamps.Add(DateTime.UtcNow); return Task.CompletedTask; },
            totalCount: 3,
            spacing: TimeSpan.FromMilliseconds(50));

        Assert.Equal(2, timestamps.Count);
        Assert.True(timestamps[1] - timestamps[0] >= TimeSpan.FromMilliseconds(40));
    }
}
