using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Issue #77 (Admin-Dashboard-Bearbeitung ohne gültige Lizenz sperren).</summary>
public class LicenseEditLockEvaluatorTests
{
    [Theory]
    [InlineData(LicenseWarningLevel.Missing)]
    [InlineData(LicenseWarningLevel.Invalid)]
    [InlineData(LicenseWarningLevel.Expired)]
    public void IsEditingLocked_ReturnsTrue_ForMissingInvalidExpired(LicenseWarningLevel level)
    {
        Assert.True(LicenseEditLockEvaluator.IsEditingLocked(level));
    }

    [Theory]
    [InlineData(LicenseWarningLevel.None)]
    [InlineData(LicenseWarningLevel.ExpiringSoon)]
    public void IsEditingLocked_ReturnsFalse_ForNoneAndExpiringSoon(LicenseWarningLevel level)
    {
        // ExpiringSoon (Nutzervorgabe, Issue #77): die Lizenz ist bis zum Ablauf noch gültig -
        // die reine Vorwarnung darf die Bearbeitung nicht schon vorab sperren.
        Assert.False(LicenseEditLockEvaluator.IsEditingLocked(level));
    }
}
