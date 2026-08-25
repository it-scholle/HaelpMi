using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

public class LicenseWarningStateStoreTests
{
    [Fact]
    public void Load_NoFileYet_ReturnsEmptyState()
    {
        using var scope = new TestAppDataScope();
        var store = new LicenseWarningStateStore();

        var state = store.Load();

        Assert.Null(state.DismissedStage);
        Assert.Null(state.SnoozedUntilUtc);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsDismissedStageAndSnooze()
    {
        using var scope = new TestAppDataScope();
        var store = new LicenseWarningStateStore();
        var until = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        store.Save(new LicenseWarningReminderState { DismissedStage = LicenseWarningStage.Reminder, SnoozedUntilUtc = until });
        var loaded = store.Load();

        Assert.Equal(LicenseWarningStage.Reminder, loaded.DismissedStage);
        Assert.Equal(until, loaded.SnoozedUntilUtc);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToEmptyStateInsteadOfThrowing()
    {
        using var scope = new TestAppDataScope();
        HaelpMi.Core.Storage.AppPaths.EnsureRootExists();
        File.WriteAllText(HaelpMi.Core.Storage.AppPaths.LicenseWarningStateFilePath, "{ not valid json");
        var store = new LicenseWarningStateStore();

        var state = store.Load();

        Assert.Null(state.DismissedStage);
    }
}
