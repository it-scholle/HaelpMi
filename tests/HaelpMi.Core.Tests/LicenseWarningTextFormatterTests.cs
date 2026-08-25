using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

public class LicenseWarningTextFormatterTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SubtextFor_UpcomingExpiry_DateFirstThenDaysInParens()
    {
        var expiresAt = Now.AddDays(24);

        var text = LicenseWarningTextFormatter.SubtextFor(LicenseWarningStage.Reminder, expiresAt, Now);

        Assert.Equal("18.09.2026 (noch 24 Tage)", text);
    }

    [Fact]
    public void SubtextFor_OneDayLeft_UsesSingular()
    {
        var expiresAt = Now.AddDays(1);

        var text = LicenseWarningTextFormatter.SubtextFor(LicenseWarningStage.Urgent, expiresAt, Now);

        Assert.Equal("26.08.2026 (noch 1 Tag)", text);
    }

    [Fact]
    public void SubtextFor_Expired_UsesSeitVorWording()
    {
        var expiresAt = Now.AddDays(-12);

        var text = LicenseWarningTextFormatter.SubtextFor(LicenseWarningStage.Expired, expiresAt, Now);

        Assert.Equal("seit 13.08.2026 (vor 12 Tagen)", text);
    }

    [Fact]
    public void SubtextFor_ExpiredOneDayAgo_UsesSingular()
    {
        var expiresAt = Now.AddDays(-1);

        var text = LicenseWarningTextFormatter.SubtextFor(LicenseWarningStage.Expired, expiresAt, Now);

        Assert.Equal("seit 24.08.2026 (vor 1 Tag)", text);
    }
}
