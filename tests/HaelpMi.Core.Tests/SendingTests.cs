using HaelpMi.Core.Models;
using HaelpMi.Core.Sending;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Covers the FR-7 extension point (Pflichtenheft 5.9): still always sends immediately,
/// with no confirmation prompt, even after the Teil-2 rename to AlarmProfile.
/// </summary>
public class SendingTests
{
    [Fact]
    public async Task NoConfirmation_ReturnsTrue_WithoutAnyPrompt()
    {
        var hook = NoConfirmation.Instance;
        var profile = new AlarmProfile { Text = "Test" };
        var targets = new List<DeviceEntry>();

        var result = await hook.ConfirmAsync(profile, targets, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public void NoConfirmation_Instance_IsASingleton()
    {
        Assert.Same(NoConfirmation.Instance, NoConfirmation.Instance);
    }
}
