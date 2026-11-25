using HaelpMi.Core.Ipc;
using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

public class SelfTestFeedbackTests
{
    [Fact]
    public void Success_ShowsNothing()
    {
        Assert.Null(SelfTestFeedback.Evaluate(new IpcResponse(true), canOpenDashboard: true));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void NoProfile_ShowsReason_AndOffersDashboardOnlyWhereItCanBeOpened(bool canOpenDashboard, bool expectedOffer)
    {
        var failure = SelfTestFeedback.Evaluate(new IpcResponse(false, SelfTestFeedback.NoProfile), canOpenDashboard);

        Assert.NotNull(failure);
        Assert.Equal(SelfTestFeedback.NoProfile, failure.Message);
        Assert.Equal(expectedOffer, failure.OfferDashboard);
    }

    [Theory]
    [InlineData(LicenseBlockReason.ManuallyDisabled)]
    [InlineData(LicenseBlockReason.NoLicense)]
    [InlineData(LicenseBlockReason.LimitExceeded)]
    public void LicenseBlock_OffersDashboard(LicenseBlockReason reason)
    {
        var failure = SelfTestFeedback.Evaluate(new IpcResponse(false, LicenseBlockTexts.Message(reason)), canOpenDashboard: true);

        Assert.NotNull(failure);
        Assert.True(failure.OfferDashboard);
    }

    [Fact]
    public void DeletedDevice_DoesNotOfferDashboard_BecauseOnlyReinstallingHelps()
    {
        var failure = SelfTestFeedback.Evaluate(new IpcResponse(false, SelfTestFeedback.DeviceRemoved), canOpenDashboard: true);

        Assert.NotNull(failure);
        Assert.False(failure.OfferDashboard);
        Assert.Contains("neu installieren", failure.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FailureWithoutReason_StillShowsAMessageWithAWayOut(string? error)
    {
        var failure = SelfTestFeedback.Evaluate(new IpcResponse(false, error), canOpenDashboard: true);

        Assert.NotNull(failure);
        Assert.Equal(SelfTestFeedback.NotReceived, failure.Message);
        Assert.False(failure.OfferDashboard);
    }

    [Fact]
    public async Task AgentNotRunning_ClientReportsHowToRestartIt()
    {
        // In der Testsitzung läuft kein Agent, die Pipe gibt es also nicht.
        var response = await new IpcClient().SendAsync(IpcCommandType.Ping, TimeSpan.FromMilliseconds(300));

        Assert.False(response.Success);
        Assert.Contains("neu öffnen", response.Error);
        Assert.NotNull(SelfTestFeedback.Evaluate(response, canOpenDashboard: true));
    }
}
