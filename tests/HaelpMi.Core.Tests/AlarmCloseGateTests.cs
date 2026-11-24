using HaelpMi.Core.Sending;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Schließen-Freigabe des Empfänger-Alarmfensters (FR-47/FR-51), ohne WPF.</summary>
public class AlarmCloseGateTests
{
    [Fact]
    public void NewGate_IsLocked_UntilFirstStatusRelay()
    {
        var gate = new AlarmCloseGate(1);

        Assert.False(gate.IsCloseAllowed);
        Assert.Equal(0, gate.OnTheWayCount);
        Assert.Equal(1, gate.MissingResponses);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ThresholdBelowOne_IsTreatedAsOne(int threshold)
    {
        var gate = new AlarmCloseGate(threshold);

        Assert.Equal(1, gate.ResponseThreshold);
        gate.ApplyStatusRelay(0, senderStillSending: true);
        Assert.False(gate.IsCloseAllowed);
    }

    [Fact]
    public void ThresholdReached_AllowsClose()
    {
        var gate = new AlarmCloseGate(1);

        gate.ApplyStatusRelay(1, senderStillSending: true);

        Assert.True(gate.IsCloseAllowed);
        Assert.Equal(0, gate.MissingResponses);
    }

    [Fact]
    public void ThresholdTwo_StaysLockedAfterFirstResponse_AndOpensWithSecond()
    {
        var gate = new AlarmCloseGate(2);

        gate.ApplyStatusRelay(1, senderStillSending: true);
        Assert.False(gate.IsCloseAllowed);
        Assert.Equal(1, gate.MissingResponses);

        gate.ApplyStatusRelay(2, senderStillSending: true);
        Assert.True(gate.IsCloseAllowed);
    }

    [Fact]
    public void SenderStopped_AllowsClose_WithoutAnyResponse()
    {
        // Abbrechen am Sender (oder Zeitablauf): danach kommt keine Welle mehr, warten wäre sinnlos.
        var gate = new AlarmCloseGate(2);

        gate.ApplyStatusRelay(0, senderStillSending: false);

        Assert.True(gate.IsCloseAllowed);
    }

    [Fact]
    public void StaleRelayWithLowerCount_DoesNotLockAgain()
    {
        var gate = new AlarmCloseGate(2);
        gate.ApplyStatusRelay(2, senderStillSending: false);

        gate.ApplyStatusRelay(1, senderStillSending: true);

        Assert.True(gate.IsCloseAllowed);
    }
}
