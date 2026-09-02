using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HaelpMi.InstallCreator;
using Xunit;

namespace HaelpMi.InstallCreator.Tests;

/// <summary>Issue #56: ein Schlüsselpaar pro Kundengruppe statt eines globalen. Umleitung auf ein temporäres Verzeichnis über LicenseKeyPairStore.RegistryFilePathOverride (Test-Hook).</summary>
public class LicenseKeyPairStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"haelpmi-keypair-test-{Guid.NewGuid():N}");

    public LicenseKeyPairStoreTests()
    {
        Directory.CreateDirectory(_tempDir);
        LicenseKeyPairStore.RegistryFilePathOverride = Path.Combine(_tempDir, "lizenzschluesselpaare.json");
    }

    public void Dispose()
    {
        LicenseKeyPairStore.RegistryFilePathOverride = null;
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Find_ReturnsNull_WhenNoEntryExistsForCustomerGroup()
    {
        Assert.Null(LicenseKeyPairStore.Find(Guid.NewGuid()));
    }

    [Fact]
    public void Append_ThenFind_RoundTripsTheMatchingEntry()
    {
        var customerGroupId = Guid.NewGuid();
        var entry = new LicenseKeyPairEntry(customerGroupId, "cHJpdmF0ZS1rZXk=", "AABBCC", DateTime.Now);

        LicenseKeyPairStore.Append(entry);
        var found = LicenseKeyPairStore.Find(customerGroupId);

        Assert.NotNull(found);
        Assert.Equal(entry.PrivateKeyBase64, found!.PrivateKeyBase64);
        Assert.Equal(entry.PublicKeyHex, found.PublicKeyHex);
    }

    [Fact]
    public void Find_DistinguishesBetweenDifferentCustomerGroups()
    {
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        LicenseKeyPairStore.Append(new LicenseKeyPairEntry(groupA, "a-key", "AA", DateTime.Now));
        LicenseKeyPairStore.Append(new LicenseKeyPairEntry(groupB, "b-key", "BB", DateTime.Now));

        Assert.Equal("a-key", LicenseKeyPairStore.Find(groupA)!.PrivateKeyBase64);
        Assert.Equal("b-key", LicenseKeyPairStore.Find(groupB)!.PrivateKeyBase64);
    }

    [Fact]
    public void Load_ReturnsEmptyList_WhenFileDoesNotExistYet()
    {
        Assert.Empty(LicenseKeyPairStore.Load());
    }
}
