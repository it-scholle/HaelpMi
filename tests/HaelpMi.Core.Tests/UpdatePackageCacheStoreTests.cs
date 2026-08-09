using HaelpMi.Core.Storage;
using HaelpMi.Core.Updates;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Nutzerwunsch 09.08.2026 (Admin-Dashboard "Updates"-Tab): welche Versionen zur Auswahl
/// stehen, kommt direkt aus <see cref="UpdatePackageCacheStore.ListAvailableVersions"/>.
/// </summary>
public class UpdatePackageCacheStoreTests
{
    private static UpdatePackageManifest MakeManifest(string version) =>
        new(version, "deadbeef", "irrelevant-signature", DateTimeOffset.UtcNow);

    [Fact]
    public void ListAvailableVersions_EmptyList_WhenNothingCachedYet()
    {
        using var scope = new TestAppDataScope();
        var store = new UpdatePackageCacheStore();

        Assert.Empty(store.ListAvailableVersions());
    }

    [Fact]
    public void ListAvailableVersions_ReturnsEverySavedVersion()
    {
        using var scope = new TestAppDataScope();
        var store = new UpdatePackageCacheStore();
        store.Save("1.0.0", MakeManifest("1.0.0"), "payload-a"u8.ToArray());
        store.Save("1.1.0", MakeManifest("1.1.0"), "payload-b"u8.ToArray());

        var versions = store.ListAvailableVersions();

        Assert.Equal(new[] { "1.0.0", "1.1.0" }, versions.OrderBy(v => v));
    }

    [Fact]
    public void ListAvailableVersions_SkipsCorruptEntries()
    {
        // Ein Verzeichnis unter updates-cache\ ohne gültiges manifest.json/package.zip
        // (z. B. ein abgebrochener Save/Import) darf die Liste nicht mit einer Version
        // füllen, die TryLoad ohnehin als "nicht vorhanden" behandeln würde.
        using var scope = new TestAppDataScope();
        var store = new UpdatePackageCacheStore();
        store.Save("1.0.0", MakeManifest("1.0.0"), "payload-a"u8.ToArray());
        Directory.CreateDirectory(Path.Combine(UpdatePackageCacheStore.DirectoryFor("2.0.0-broken")));

        var versions = store.ListAvailableVersions();

        Assert.Equal(new[] { "1.0.0" }, versions);
    }
}
