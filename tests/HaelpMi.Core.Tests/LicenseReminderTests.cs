using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Issue #20-Nacharbeit: Systemstart-Erinnerung (Toast in HaelpMi.Agent) + "Später erinnern".</summary>
public class LicenseReminderTests
{
    [Fact]
    public void IsSnoozed_False_WhenNeverSnoozed()
    {
        using var scope = new TestAppDataScope();

        Assert.False(LicenseReminderStateStore.IsSnoozed(DateTime.UtcNow));
    }

    [Fact]
    public void IsSnoozed_True_RightAfterSnoozing()
    {
        using var scope = new TestAppDataScope();

        LicenseReminderStateStore.Snooze();

        Assert.True(LicenseReminderStateStore.IsSnoozed(DateTime.UtcNow));
    }

    [Fact]
    public void IsSnoozed_False_OnceSnoozeDurationHasPassed()
    {
        using var scope = new TestAppDataScope();

        LicenseReminderStateStore.Snooze();

        var afterSnoozeExpires = DateTime.UtcNow + LicenseReminderStateStore.SnoozeDuration + TimeSpan.FromMinutes(1);
        Assert.False(LicenseReminderStateStore.IsSnoozed(afterSnoozeExpires));
    }

    [Theory]
    [InlineData(LicenseWarningLevel.Missing, "Keine Lizenz gefunden")]
    [InlineData(LicenseWarningLevel.Invalid, "Lizenz ungültig")]
    public void Format_MissingAndInvalid_NamesTheProblem(LicenseWarningLevel level, string expectedSubstring)
    {
        var text = LicenseWarningTextFormatter.Format(new LicenseWarning(level, null));

        Assert.Contains(expectedSubstring, text);
    }

    [Fact]
    public void Format_Expired_NamesDaysSinceExpiryAsAPositiveNumber()
    {
        // DaysRemaining ist bei Expired negativ (siehe LicenseWarningEvaluator) - der Text
        // muss die "seit X Tagen"-Formulierung mit dem VORZEICHEN umdrehen, sonst stünde
        // dort ein verwirrendes Minus.
        var text = LicenseWarningTextFormatter.Format(new LicenseWarning(LicenseWarningLevel.Expired, -5));

        Assert.Contains("seit 5 Tag(en) abgelaufen", text);
    }

    [Fact]
    public void Format_ExpiringSoon_NamesDaysRemaining()
    {
        var text = LicenseWarningTextFormatter.Format(new LicenseWarning(LicenseWarningLevel.ExpiringSoon, 12));

        Assert.Contains("in 12 Tag(en) ab", text);
    }

    // Issue #20 (ursprünglicher Ticket-Text: "mit Kontaktdaten") - im ersten Durchgang
    // übersehen, Nutzervorgabe 03.09.2026 nachgetragen.
    [Fact]
    public void Format_NonNoneLevel_IncludesProviderContact()
    {
        var text = LicenseWarningTextFormatter.Format(new LicenseWarning(LicenseWarningLevel.ExpiringSoon, 12));

        Assert.Contains(ProviderContact.DisplayText, text);
    }

    [Fact]
    public void Format_None_DoesNotIncludeProviderContact()
    {
        var text = LicenseWarningTextFormatter.Format(new LicenseWarning(LicenseWarningLevel.None, 200));

        Assert.DoesNotContain(ProviderContact.DisplayText, text);
    }

    [Fact]
    public void Format_None_ReturnsEmptyText()
    {
        Assert.Equal(string.Empty, LicenseWarningTextFormatter.Format(new LicenseWarning(LicenseWarningLevel.None, 200)));
    }

    // Issue #77 (Admin-Dashboard-Bearbeitung ohne gültige Lizenz sperren).
    [Theory]
    [InlineData(LicenseWarningLevel.Missing, null)]
    [InlineData(LicenseWarningLevel.Invalid, null)]
    [InlineData(LicenseWarningLevel.Expired, -5)]
    public void Format_LockedLevels_MentionEditingLock(LicenseWarningLevel level, int? daysRemaining)
    {
        var text = LicenseWarningTextFormatter.Format(new LicenseWarning(level, daysRemaining));

        Assert.Contains("Bearbeitung ist bis dahin gesperrt", text);
    }

    [Fact]
    public void Format_ExpiringSoon_DoesNotMentionEditingLock()
    {
        var text = LicenseWarningTextFormatter.Format(new LicenseWarning(LicenseWarningLevel.ExpiringSoon, 12));

        Assert.DoesNotContain("Bearbeitung ist bis dahin gesperrt", text);
    }
}
