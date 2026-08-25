using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

public class LicenseWarningEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(61, LicenseWarningStage.None)]
    [InlineData(60, LicenseWarningStage.EarlyNotice)]
    [InlineData(31, LicenseWarningStage.EarlyNotice)]
    [InlineData(30, LicenseWarningStage.Reminder)]
    [InlineData(8, LicenseWarningStage.Reminder)]
    [InlineData(7, LicenseWarningStage.Urgent)]
    [InlineData(1, LicenseWarningStage.Urgent)]
    [InlineData(0, LicenseWarningStage.Expired)]
    [InlineData(-12, LicenseWarningStage.Expired)]
    public void GetStage_MapsDaysRemainingToStage(int daysUntilExpiry, LicenseWarningStage expected)
    {
        var expiresAt = Now.AddDays(daysUntilExpiry);

        Assert.Equal(expected, LicenseWarningEvaluator.GetStage(expiresAt, Now));
    }

    [Fact]
    public void ShouldShowToast_NoneStage_NeverShows()
    {
        Assert.False(LicenseWarningEvaluator.ShouldShowToast(LicenseWarningStage.None, null, Now));
    }

    [Fact]
    public void ShouldShowToast_NewStage_ShowsEvenWithoutState()
    {
        Assert.True(LicenseWarningEvaluator.ShouldShowToast(LicenseWarningStage.EarlyNotice, null, Now));
    }

    [Fact]
    public void ShouldShowToast_IgnoredSameStage_Suppressed()
    {
        var state = new LicenseWarningReminderState { DismissedStage = LicenseWarningStage.Reminder, SnoozedUntilUtc = null };

        Assert.False(LicenseWarningEvaluator.ShouldShowToast(LicenseWarningStage.Reminder, state, Now));
    }

    [Fact]
    public void ShouldShowToast_IgnoredPreviousStage_ShowsAgainOnEscalation()
    {
        var state = new LicenseWarningReminderState { DismissedStage = LicenseWarningStage.EarlyNotice, SnoozedUntilUtc = null };

        Assert.True(LicenseWarningEvaluator.ShouldShowToast(LicenseWarningStage.Reminder, state, Now));
    }

    [Fact]
    public void ShouldShowToast_SnoozeNotYetElapsed_Suppressed()
    {
        var state = new LicenseWarningReminderState
        {
            DismissedStage = LicenseWarningStage.Urgent,
            SnoozedUntilUtc = Now.AddDays(1),
        };

        Assert.False(LicenseWarningEvaluator.ShouldShowToast(LicenseWarningStage.Urgent, state, Now));
    }

    [Fact]
    public void ShouldShowToast_SnoozeElapsed_ShowsAgain()
    {
        var state = new LicenseWarningReminderState
        {
            DismissedStage = LicenseWarningStage.Urgent,
            SnoozedUntilUtc = Now.AddMinutes(-1),
        };

        Assert.True(LicenseWarningEvaluator.ShouldShowToast(LicenseWarningStage.Urgent, state, Now));
    }

    [Fact]
    public void ShouldShowToast_ExpiredStage_AlwaysShowsRegardlessOfState()
    {
        var state = new LicenseWarningReminderState
        {
            DismissedStage = LicenseWarningStage.Expired,
            SnoozedUntilUtc = Now.AddYears(1),
        };

        Assert.True(LicenseWarningEvaluator.ShouldShowToast(LicenseWarningStage.Expired, state, Now));
    }
}
