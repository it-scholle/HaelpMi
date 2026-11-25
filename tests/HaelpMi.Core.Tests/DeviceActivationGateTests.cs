using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
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

    [Fact]
    public void OverrideAfterToggle_Activate_ReturnsNone()
    {
        // Aktivieren hebt nur die manuelle Deaktivierung auf; ForceEnabled würde das Kontingent umgehen.
        Assert.Equal(LicenseOverride.None, DeviceActivationGate.OverrideAfterToggle(activate: true));
    }

    [Fact]
    public void OverrideAfterToggle_Deactivate_ReturnsForceDisabled()
    {
        Assert.Equal(LicenseOverride.ForceDisabled, DeviceActivationGate.OverrideAfterToggle(activate: false));
    }

    [Fact]
    public void ReactivatedDevice_WithoutLicense_StaysOutsideQuota()
    {
        var reactivated = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), DateTimeOffset.UtcNow, DeviceActivationGate.OverrideAfterToggle(activate: true));

        Assert.False(LicenseLimitEvaluator.IsWithinLimit(reactivated.DeviceId, new[] { reactivated }, userLimit: 0));
    }

    [Fact]
    public void ReactivatedDevice_WithFreeSeat_IsWithinQuota()
    {
        var reactivated = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), DateTimeOffset.UtcNow, DeviceActivationGate.OverrideAfterToggle(activate: true));

        Assert.True(LicenseLimitEvaluator.IsWithinLimit(reactivated.DeviceId, new[] { reactivated }, userLimit: 1));
    }
}
