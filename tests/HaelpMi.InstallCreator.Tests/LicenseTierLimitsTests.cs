using HaelpMi.InstallCreator.Controls;
using Xunit;

namespace HaelpMi.InstallCreator.Tests;

public class LicenseTierLimitsTests
{
    [Theory]
    [InlineData(LicenseTier.Trial, 10)]
    [InlineData(LicenseTier.S, 25)]
    [InlineData(LicenseTier.M, 75)]
    [InlineData(LicenseTier.L, 150)]
    public void GetUserLimit_ReturnsFrozenValueFromIssue19(LicenseTier tier, int expectedLimit)
    {
        Assert.Equal(expectedLimit, LicenseTierLimits.GetUserLimit(tier));
    }

    [Fact]
    public void GetUserLimit_Xl_IsUnbegrenzt()
    {
        Assert.Null(LicenseTierLimits.GetUserLimit(LicenseTier.XL));
    }

    [Fact]
    public void GetDisplayLabel_Xl_SaysUnbegrenztNotANumber()
    {
        Assert.Equal("XL - unbegrenzt", LicenseTierLimits.GetDisplayLabel(LicenseTier.XL));
    }

    [Fact]
    public void GetDisplayLabel_LimitedTier_ContainsTheNumber()
    {
        Assert.Equal("M - 75 Nutzer", LicenseTierLimits.GetDisplayLabel(LicenseTier.M));
    }

    [Fact]
    public void GetDisplayLabel_Trial_ShowsXsTrialNotJustTrial()
    {
        // Offizielles XS-Paket hat dasselbe Nutzerlimit wie Trial - eine gemeinsame Stufe
        // im Dropdown statt zweier Einträge mit identischem Limit.
        Assert.Equal("XS/Trial - 10 Nutzer", LicenseTierLimits.GetDisplayLabel(LicenseTier.Trial));
    }
}
