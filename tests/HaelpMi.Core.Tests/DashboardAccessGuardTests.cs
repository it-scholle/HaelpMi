using HaelpMi.Core.Models;
using HaelpMi.Core.Runtime;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Deckt Issue #10 ab: das Admin-Dashboard darf nur beim installierenden Windows-Nutzer
/// sichtbar/startbar sein. Der Fast-User-Switching-Teil des Testplan-Hinweises im Issue
/// (zwei echte Windows-Konten an derselben Maschine) bleibt bewusst ein manueller Test -
/// hier nur der codeseitig automatisierbare Kern (Rollen-/Namensabgleich, fail-closed).
/// </summary>
public class DashboardAccessGuardTests
{
    [Fact]
    public void CurrentUserMayOpenDashboard_ReturnsFalse_ForUserRole_EvenWithMatchingName()
    {
        Assert.False(DashboardAccessGuard.CurrentUserMayOpenDashboard(Role.User, "Fred", "Fred"));
    }

    [Fact]
    public void CurrentUserMayOpenDashboard_ReturnsTrue_ForAdminRole_WhenNamesMatch()
    {
        Assert.True(DashboardAccessGuard.CurrentUserMayOpenDashboard(Role.Admin, "Fred", "Fred"));
    }

    [Fact]
    public void CurrentUserMayOpenDashboard_ReturnsFalse_ForAdminRole_AfterFastUserSwitchToADifferentAccount()
    {
        // Der Admin (Fred) hat installiert, ein anderer Windows-Nutzer (Wilma) ist per Fast
        // User Switching an dieselbe Maschine angemeldet.
        Assert.False(DashboardAccessGuard.CurrentUserMayOpenDashboard(Role.Admin, "Fred", "Wilma"));
    }

    [Fact]
    public void CurrentUserMayOpenDashboard_IsCaseInsensitive_LikeWindowsAccountNames()
    {
        Assert.True(DashboardAccessGuard.CurrentUserMayOpenDashboard(Role.Admin, "Fred", "FRED"));
    }

    [Fact]
    public void CurrentUserMayOpenDashboard_FailsClosed_WhenInstalledByInfoIsMissing()
    {
        // z. B. eine vor Issue #10 installierte Bestandsmaschine ohne installed-by.json -
        // fail-closed statt fail-open, siehe DashboardAccessGuard-Kommentar.
        Assert.False(DashboardAccessGuard.CurrentUserMayOpenDashboard(Role.Admin, installingUserName: null, currentUserName: "Fred"));
    }

    [Fact]
    public void InstalledByInfoStore_TryLoad_ReturnsNull_WhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), "HaelpMiTests_" + Guid.NewGuid().ToString("N") + ".json");

        Assert.Null(InstalledByInfoStore.TryLoad(path));
    }

    [Fact]
    public void InstalledByInfoStore_TryLoad_ReturnsNull_ForCorruptedFile_InsteadOfThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), "HaelpMiTests_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ das ist kein gueltiges JSON");

        try
        {
            Assert.Null(InstalledByInfoStore.TryLoad(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InstalledByInfoStore_RoundTrips_InstallingUserName()
    {
        var path = Path.Combine(Path.GetTempPath(), "HaelpMiTests_" + Guid.NewGuid().ToString("N") + ".json");
        JsonFileStore.Save(path, new InstalledByInfo { InstallingUserName = "Fred" });

        try
        {
            var reloaded = InstalledByInfoStore.TryLoad(path);

            Assert.NotNull(reloaded);
            Assert.Equal("Fred", reloaded!.InstallingUserName);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
