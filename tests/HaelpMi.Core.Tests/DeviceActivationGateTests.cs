using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Issue #61-Nacharbeit ("ich kann auf 3/2 Lizenzen gehen" - darf nicht möglich sein).</summary>
public class DeviceActivationGateTests
{
    [Fact]
    public void CanToggle_True_WhenActiveCountBelowLimit()
    {
        Assert.True(DeviceActivationGate.CanToggle(isActive: false, activeCount: 1, seatLimit: 2));
    }

    [Fact]
    public void CanToggle_False_WhenActiveCountAlreadyAtLimit()
    {
        Assert.False(DeviceActivationGate.CanToggle(isActive: false, activeCount: 2, seatLimit: 2));
    }

    [Fact]
    public void CanToggle_False_WhenActiveCountAboveLimit()
    {
        // bereits über dem Kontingent (Altbestand von vor diesem Fix) - trotzdem kein
        // weiteres Aktivieren erlauben.
        Assert.False(DeviceActivationGate.CanToggle(isActive: false, activeCount: 3, seatLimit: 2));
    }

    [Fact]
    public void CanToggle_True_ForAlreadyActiveDevice_EvenAtCapacity()
    {
        // Ein bereits aktives Gerät muss sich immer deaktivieren lassen - sonst gäbe es
        // keinen Weg mehr, das Kontingent wieder freizuräumen.
        Assert.True(DeviceActivationGate.CanToggle(isActive: true, activeCount: 2, seatLimit: 2));
    }

    [Fact]
    public void CanToggle_True_WhenSeatLimitIsUnbounded()
    {
        Assert.True(DeviceActivationGate.CanToggle(isActive: false, activeCount: 500, seatLimit: null));
    }
}
