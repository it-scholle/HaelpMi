using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Issue #94: reine Vergleichsregel, ausschließlich anhand der Gerätelizenz-Anzahl.</summary>
public class LicensePackageComparerTests
{
    private static License MakeLicense(int? userLimit) =>
        new(Guid.NewGuid(), LicenseTier.Custom, userLimit, DateTime.UtcNow, DateTime.UtcNow.AddYears(1), SignatureBase64: "x");

    [Theory]
    [InlineData(10, 25, true)]  // weniger Geräte als aktuell -> Downgrade
    [InlineData(25, 25, false)] // gleiche Anzahl -> kein Downgrade (Upgrade-Pfad)
    [InlineData(50, 25, false)] // mehr Geräte -> Upgrade
    public void IsDowngrade_ComparesUserLimitOnly(int candidateLimit, int currentLimit, bool expectedDowngrade)
    {
        var result = LicensePackageComparer.IsDowngrade(MakeLicense(candidateLimit), MakeLicense(currentLimit));

        Assert.Equal(expectedDowngrade, result);
    }

    [Fact]
    public void IsDowngrade_ReturnsTrue_WhenCurrentIsUnlimitedAndCandidateIsNot()
    {
        var result = LicensePackageComparer.IsDowngrade(MakeLicense(25), MakeLicense(null));

        Assert.True(result);
    }

    [Fact]
    public void IsDowngrade_ReturnsFalse_WhenCandidateBecomesUnlimited()
    {
        var result = LicensePackageComparer.IsDowngrade(MakeLicense(null), MakeLicense(25));

        Assert.False(result);
    }

    [Fact]
    public void IsDowngrade_ReturnsFalse_WhenBothUnlimited()
    {
        var result = LicensePackageComparer.IsDowngrade(MakeLicense(null), MakeLicense(null));

        Assert.False(result);
    }
}
