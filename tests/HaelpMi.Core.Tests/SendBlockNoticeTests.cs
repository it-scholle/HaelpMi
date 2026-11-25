using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Tastenkürzel auf einem gesperrten Gerät: statt zu senden erscheint ein Hinweis mit dem Grund.</summary>
public class SendBlockNoticeTests
{
    private static LiveIdentity UserIdentity(Guid deviceId) =>
        new(Guid.NewGuid(), deviceId, "PC", "User", "Raum", "1", Role.User, false, "1.0.0", 0, DateTimeOffset.UtcNow);

    private static LicenseCheckResult ValidLicense(int userLimit) =>
        new(LicenseStatus.Valid, new License(Guid.NewGuid(), LicenseTier.Custom, userLimit, DateTime.UtcNow, DateTime.UtcNow.AddYears(1), "irrelevant"));

    [Fact]
    public void For_NoNotice_WhenDeviceMaySend()
    {
        Assert.Null(SendBlockNotice.For(deviceRemoved: false, LicenseBlockReason.None));
    }

    [Theory]
    [InlineData(LicenseBlockReason.ManuallyDisabled)]
    [InlineData(LicenseBlockReason.NoLicense)]
    [InlineData(LicenseBlockReason.LimitExceeded)]
    public void For_NamesTheBlockReason(LicenseBlockReason reason)
    {
        var notice = SendBlockNotice.For(deviceRemoved: false, reason);

        Assert.NotNull(notice);
        Assert.Equal(LicenseBlockTexts.Title(reason), notice!.Title);
        Assert.Equal(LicenseBlockTexts.Message(reason), notice.Message);
    }

    [Fact]
    public void For_RemovedDeviceWins_OverLicenseReason()
    {
        var notice = SendBlockNotice.For(deviceRemoved: true, LicenseBlockReason.NoLicense);

        Assert.NotNull(notice);
        Assert.Equal("Gerät entfernt", notice!.Title);
    }

    [Fact]
    public void ManuallyDisabledDevice_GetsDisabledNotice_EvenWithFreeSeats()
    {
        var identity = UserIdentity(Guid.NewGuid()) with
        {
            LicenseOverride = LicenseOverride.ForceDisabled,
            LicenseOverrideSetAtUtc = DateTimeOffset.UtcNow,
        };
        var guard = new LicenseLimitGuard(() => identity, () => ValidLicense(10), () => new List<DeviceEntry>());

        var notice = SendBlockNotice.For(deviceRemoved: false, guard.GetOwnBlockReason());

        Assert.Equal(LicenseBlockTexts.Title(LicenseBlockReason.ManuallyDisabled), notice?.Title);
    }

    [Fact]
    public void UserDeviceWithoutLicense_GetsNoLicenseNotice()
    {
        var identity = UserIdentity(Guid.NewGuid());
        var guard = new LicenseLimitGuard(() => identity, () => new LicenseCheckResult(LicenseStatus.Missing, null), () => new List<DeviceEntry>());

        var notice = SendBlockNotice.For(deviceRemoved: false, guard.GetOwnBlockReason());

        Assert.Equal(LicenseBlockTexts.Title(LicenseBlockReason.NoLicense), notice?.Title);
    }
}
