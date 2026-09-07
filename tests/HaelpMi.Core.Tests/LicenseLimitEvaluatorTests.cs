using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
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

    // --- LicenseOverride (vorbereiteter Erweiterungspunkt für Issue #61) ---

    [Fact]
    public void IsWithinLimit_ForceDisabled_IsAlwaysDisabled_EvenIfItWouldRankWithinLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var forceDisabled = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now, LicenseOverride.ForceDisabled);
        var other = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddSeconds(1));
        var known = new[] { forceDisabled, other };

        Assert.False(LicenseLimitEvaluator.IsWithinLimit(forceDisabled.DeviceId, known, userLimit: 5));
    }

    [Fact]
    public void IsWithinLimit_ForceDisabled_FreesUpASlot_ForTheNextRankedDevice()
    {
        var now = DateTimeOffset.UtcNow;
        var forceDisabled = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now, LicenseOverride.ForceDisabled);
        var second = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddSeconds(1));
        var third = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddSeconds(2));
        var known = new[] { forceDisabled, second, third };

        // userLimit 1: ohne den Override wäre "third" überzählig - ForceDisabled auf dem
        // ältesten Gerät gibt den Platz stattdessen frei (Issue #61: "Lizenz entziehen, um
        // andere Geräte aufnehmen zu können").
        Assert.True(LicenseLimitEvaluator.IsWithinLimit(second.DeviceId, known, userLimit: 1));
        Assert.False(LicenseLimitEvaluator.IsWithinLimit(third.DeviceId, known, userLimit: 1));
    }

    [Fact]
    public void IsWithinLimit_ForceEnabled_IsAlwaysEnabled_EvenIfItWouldRankBeyondLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var oldest = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now);
        var forceEnabled = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddSeconds(1), LicenseOverride.ForceEnabled);
        var known = new[] { oldest, forceEnabled };

        Assert.True(LicenseLimitEvaluator.IsWithinLimit(forceEnabled.DeviceId, known, userLimit: 1));
    }

    [Fact]
    public void IsWithinLimit_ForceEnabled_StillConsumesAContingentSlot()
    {
        var now = DateTimeOffset.UtcNow;
        var forceEnabled = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now, LicenseOverride.ForceEnabled);
        var oldest = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddSeconds(1));
        var secondOldest = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), now.AddSeconds(2));
        var known = new[] { forceEnabled, oldest, secondOldest };

        // userLimit 2: ForceEnabled belegt einen der zwei Plätze fest - von den beiden
        // Geräten OHNE Override passt dadurch nur noch das ältere hinein, nicht beide
        // (ein Override darf das Kontingent selbst nicht aushebeln).
        Assert.True(LicenseLimitEvaluator.IsWithinLimit(forceEnabled.DeviceId, known, userLimit: 2));
        Assert.True(LicenseLimitEvaluator.IsWithinLimit(oldest.DeviceId, known, userLimit: 2));
        Assert.False(LicenseLimitEvaluator.IsWithinLimit(secondOldest.DeviceId, known, userLimit: 2));
    }

    [Fact]
    public void GetDisabledDeviceIds_IncludesForceDisabled_EvenWhenLicenseIsUnlimited()
    {
        var forceDisabled = new LicenseLimitEvaluator.DeviceSeen(Guid.NewGuid(), DateTimeOffset.UtcNow, LicenseOverride.ForceDisabled);
        var known = new[] { forceDisabled, Device(DateTimeOffset.UtcNow) };

        var disabled = LicenseLimitEvaluator.GetDisabledDeviceIds(known, userLimit: null);

        Assert.Equal(new HashSet<Guid> { forceDisabled.DeviceId }, disabled);
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
