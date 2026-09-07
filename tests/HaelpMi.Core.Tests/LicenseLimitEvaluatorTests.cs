using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Issue #59/#60: deckt die dezentrale "wer ist lizenzüberschritten"-Regel ab (siehe LicenseLimitEvaluator).</summary>
public class LicenseLimitEvaluatorTests
{
    private static LicenseLimitEvaluator.DeviceSeen Device(DateTimeOffset firstSeenUtc) => new(Guid.NewGuid(), firstSeenUtc);

    [Fact]
    public void IsWithinLimit_True_ForEveryone_WhenUserLimitIsNull()
    {
        var deviceId = Guid.NewGuid();
        var known = new[] { new LicenseLimitEvaluator.DeviceSeen(deviceId, DateTimeOffset.UtcNow) };

        Assert.True(LicenseLimitEvaluator.IsWithinLimit(deviceId, known, userLimit: null));
    }

    [Fact]
    public void IsWithinLimit_KeepsOldestDevices_DisablesNewestOnes_WhenOverLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var oldest = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddDays(-3));
        var middle = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddDays(-2));
        var newest = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddDays(-1));
        var known = new[] { newest, oldest, middle }; // Reihenfolge in der Liste darf keine Rolle spielen

        Assert.True(LicenseLimitEvaluator.IsWithinLimit(oldest.DeviceId, known, userLimit: 2));
        Assert.True(LicenseLimitEvaluator.IsWithinLimit(middle.DeviceId, known, userLimit: 2));
        Assert.False(LicenseLimitEvaluator.IsWithinLimit(newest.DeviceId, known, userLimit: 2));
    }

    [Fact]
    public void IsWithinLimit_True_WhenExactlyAtLimit()
    {
        var known = new[] { Device(DateTimeOffset.UtcNow), Device(DateTimeOffset.UtcNow.AddSeconds(1)) };

        Assert.True(LicenseLimitEvaluator.IsWithinLimit(known[0].DeviceId, known, userLimit: 2));
        Assert.True(LicenseLimitEvaluator.IsWithinLimit(known[1].DeviceId, known, userLimit: 2));
    }

    [Fact]
    public void IsWithinLimit_False_ForUnknownDevice()
    {
        var known = new[] { Device(DateTimeOffset.UtcNow) };

        Assert.False(LicenseLimitEvaluator.IsWithinLimit(Guid.NewGuid(), known, userLimit: 5));
    }

    [Fact]
    public void GetDisabledDeviceIds_Empty_WhenUserLimitIsNull()
    {
        var known = new[] { Device(DateTimeOffset.UtcNow), Device(DateTimeOffset.UtcNow) };

        Assert.Empty(LicenseLimitEvaluator.GetDisabledDeviceIds(known, userLimit: null));
    }

    [Fact]
    public void GetDisabledDeviceIds_ContainsOnlyDevicesBeyondLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var a = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now);
        var b = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddSeconds(1));
        var c = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddSeconds(2));
        var known = new[] { a, b, c };

        var disabled = LicenseLimitEvaluator.GetDisabledDeviceIds(known, userLimit: 1);

        Assert.Equal(new HashSet<Guid> { b.DeviceId, c.DeviceId }, disabled);
    }

    [Fact]
    public void IsWithinLimit_IsDeterministic_ForExactFirstSeenUtcTies()
    {
        // Zwei Geräte mit identischem FirstSeenUtc (theoretisch möglich, z. B. beide über
        // denselben lokalen Empfangszeitpunkt-Fallback) - die zusätzliche Sortierung nach
        // DeviceId muss trotzdem ein stabiles, wiederholbares Ergebnis liefern.
        var sameTimestamp = DateTimeOffset.UtcNow;
        var known = new[] { Device(sameTimestamp), Device(sameTimestamp), Device(sameTimestamp) };

        var disabledFirstRun = LicenseLimitEvaluator.GetDisabledDeviceIds(known, userLimit: 2);
        var disabledSecondRun = LicenseLimitEvaluator.GetDisabledDeviceIds(known, userLimit: 2);

        Assert.Equal(disabledFirstRun, disabledSecondRun);
        Assert.Single(disabledFirstRun);
    }
}
