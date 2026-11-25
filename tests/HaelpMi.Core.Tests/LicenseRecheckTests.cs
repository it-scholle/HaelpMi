using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Lizenzablauf und "läuft bald ab" im laufenden Betrieb erkennen, Hinweis auch auf User-Geräten.</summary>
public class LicenseRecheckTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    private static LicenseCheckResult Valid(DateTime expiryUtc) =>
        new(LicenseStatus.Valid, new License(Guid.NewGuid(), LicenseTier.Custom, 5, Now.AddDays(-100), expiryUtc, "sig"));

    private static LicenseCheckResult Expired(DateTime expiryUtc) =>
        new(LicenseStatus.Expired, new License(Guid.NewGuid(), LicenseTier.Custom, 5, Now.AddDays(-100), expiryUtc, "sig"));

    [Fact]
    public void NextCheck_ValidLicenseFarAway_IsAtMostOneDay()
    {
        Assert.Equal(Now + LicenseRecheckSchedule.MaxInterval, LicenseRecheckSchedule.NextCheckUtc(Valid(Now.AddDays(200)), Now));
    }

    [Fact]
    public void NextCheck_WarningThresholdWithinOneDay_IsAtWarningThreshold()
    {
        var expiry = Now.AddDays(LicenseWarningEvaluator.WarningWindowDays + 1).AddHours(3);
        var next = LicenseRecheckSchedule.NextCheckUtc(Valid(expiry), Now);

        Assert.True(next > Now.AddHours(3) && next <= Now.AddHours(3).AddSeconds(5), $"next={next:O}");
        Assert.Equal(LicenseWarningLevel.None, LicenseWarningEvaluator.Evaluate(Valid(expiry), Now).Level);
        Assert.Equal(LicenseWarningLevel.ExpiringSoon, LicenseWarningEvaluator.Evaluate(Valid(expiry), next).Level);
    }

    [Fact]
    public void NextCheck_ExpiryWithinOneDay_IsJustAfterExpiry()
    {
        var expiry = Now.AddMinutes(7);

        var next = LicenseRecheckSchedule.NextCheckUtc(Valid(expiry), Now);

        // LicenseReader wertet erst "Ablauf < jetzt" als abgelaufen.
        Assert.True(next > expiry && next <= expiry.AddSeconds(5), $"next={next:O}");
    }

    [Theory]
    [InlineData(LicenseStatus.Missing)]
    [InlineData(LicenseStatus.Invalid)]
    public void NextCheck_WithoutUsableLicense_IsOneDay(LicenseStatus status)
    {
        Assert.Equal(Now + LicenseRecheckSchedule.MaxInterval, LicenseRecheckSchedule.NextCheckUtc(new LicenseCheckResult(status, null), Now));
    }

    [Fact]
    public void NextCheck_ExpiredLicense_IsOneDay()
    {
        Assert.Equal(Now + LicenseRecheckSchedule.MaxInterval, LicenseRecheckSchedule.NextCheckUtc(Expired(Now.AddDays(-1)), Now));
    }

    [Fact]
    public void Trigger_AdminAtStart_ShowsExpiringSoonUnlessSnoozed()
    {
        Assert.True(LicenseReminderTrigger.ShouldShow(null, LicenseWarningLevel.ExpiringSoon, isUserDevice: false, snoozed: false));
        Assert.False(LicenseReminderTrigger.ShouldShow(null, LicenseWarningLevel.ExpiringSoon, isUserDevice: false, snoozed: true));
        Assert.False(LicenseReminderTrigger.ShouldShow(null, LicenseWarningLevel.None, isUserDevice: false, snoozed: false));
    }

    [Fact]
    public void Trigger_AdminWhileRunning_ShowsOnLevelChangeEvenIfSnoozed()
    {
        Assert.True(LicenseReminderTrigger.ShouldShow(LicenseWarningLevel.ExpiringSoon, LicenseWarningLevel.Expired, isUserDevice: false, snoozed: true));
        Assert.True(LicenseReminderTrigger.ShouldShow(LicenseWarningLevel.None, LicenseWarningLevel.ExpiringSoon, isUserDevice: false, snoozed: false));
    }

    [Fact]
    public void Trigger_WhileRunning_SameLevel_DoesNotRepeat()
    {
        Assert.False(LicenseReminderTrigger.ShouldShow(LicenseWarningLevel.ExpiringSoon, LicenseWarningLevel.ExpiringSoon, isUserDevice: false, snoozed: false));
        Assert.False(LicenseReminderTrigger.ShouldShow(LicenseWarningLevel.Expired, LicenseWarningLevel.Expired, isUserDevice: true, snoozed: false));
    }

    [Fact]
    public void Trigger_UserDevice_ShowsOnlyExpired()
    {
        Assert.True(LicenseReminderTrigger.ShouldShow(null, LicenseWarningLevel.Expired, isUserDevice: true, snoozed: false));
        Assert.True(LicenseReminderTrigger.ShouldShow(LicenseWarningLevel.ExpiringSoon, LicenseWarningLevel.Expired, isUserDevice: true, snoozed: false));
        Assert.False(LicenseReminderTrigger.ShouldShow(null, LicenseWarningLevel.ExpiringSoon, isUserDevice: true, snoozed: false));
        // Fehlende oder ungültige Lizenz sperrt das User-Gerät; dafür gibt es den Sperr-Hinweis.
        Assert.False(LicenseReminderTrigger.ShouldShow(null, LicenseWarningLevel.Missing, isUserDevice: true, snoozed: false));
    }

    [Fact]
    public void UserDeviceText_Expired_SaysAlarmsContinueAndNamesAdmin()
    {
        var text = LicenseWarningTextFormatter.FormatForUserDevice(new LicenseWarning(LicenseWarningLevel.Expired, -3));

        Assert.Contains("seit 3 Tag(en) abgelaufen", text);
        Assert.Contains("Alarme weiter", text);
        Assert.Contains("Admin", text);
        Assert.DoesNotContain("Bearbeitung", text);
    }
}
