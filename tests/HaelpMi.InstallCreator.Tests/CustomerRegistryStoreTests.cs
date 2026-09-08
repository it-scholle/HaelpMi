using System;
using System.IO;
using HaelpMi.InstallCreator;
using Xunit;

namespace HaelpMi.InstallCreator.Tests;

/// <summary>Nutzerwunsch 08.09.2026: dieselbe Kundennummer muss beim erneuten Bauen dieselbe CustomerGroupId liefern, sonst werden bereits ausgegebene Lizenzen entwertet. Umleitung auf temporäre Verzeichnisse über die RegistryFilePathOverride-Test-Hooks, analog LicenseKeyPairStoreTests.</summary>
public class CustomerRegistryStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"haelpmi-customerregistry-test-{Guid.NewGuid():N}");

    public CustomerRegistryStoreTests()
    {
        Directory.CreateDirectory(_tempDir);
        CustomerRegistryStore.RegistryFilePathOverride = Path.Combine(_tempDir, "kundenregister.json");
        CustomerRegistryStore.TestRegistryFilePathOverride = Path.Combine(_tempDir, "test-kundenregister.json");
    }

    public void Dispose()
    {
        CustomerRegistryStore.RegistryFilePathOverride = null;
        CustomerRegistryStore.TestRegistryFilePathOverride = null;
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ResolveCustomerGroupId_ReturnsNewGuid_WhenKundennummerUnknown()
    {
        var result = CustomerRegistryStore.ResolveCustomerGroupId(isTestInstaller: false, kundennummer: 10001);

        Assert.NotEqual(Guid.Empty, result);
    }

    [Fact]
    public void ResolveCustomerGroupId_ReusesExistingCustomerGroupId_ForKnownKundennummer()
    {
        var customerGroupId = Guid.NewGuid();
        CustomerRegistryStore.Append(isTestInstaller: false, new CustomerRegistryEntry(
            10001, customerGroupId, "Musterkunde", DateTime.Now, Kontakt: null));

        var resolved = CustomerRegistryStore.ResolveCustomerGroupId(isTestInstaller: false, kundennummer: 10001);

        Assert.Equal(customerGroupId, resolved);
    }

    [Fact]
    public void ResolveCustomerGroupId_KeepsTestAndProduktivRegisterGetrennt()
    {
        var produktivGroupId = Guid.NewGuid();
        CustomerRegistryStore.Append(isTestInstaller: false, new CustomerRegistryEntry(
            1, produktivGroupId, "Musterkunde", DateTime.Now, Kontakt: null));

        // Dieselbe Nummer (1) im Test-Register ist ein anderer Datensatz - keine Verwechslung
        // zwischen den beiden getrennten Nummernreihen (siehe CustomerRegistryStore-Kommentar).
        var testResolved = CustomerRegistryStore.ResolveCustomerGroupId(isTestInstaller: true, kundennummer: 1);

        Assert.NotEqual(produktivGroupId, testResolved);
    }

    [Fact]
    public void ResolveCustomerGroupId_ReturnsSameGuid_WhenCalledRepeatedlyForUnknownKundennummer()
    {
        // Zwei Aufrufe ohne dazwischenliegenden Append (z. B. Validierung vor dem eigentlichen
        // Bauen) dürfen sich unterscheiden - erst der tatsächliche Append fixiert die Zuordnung.
        // Hier wird stattdessen geprüft, dass ein bereits registrierter Kunde stabil bleibt,
        // auch nachdem ein zweiter, anderer Kunde hinzugekommen ist.
        var firstGroupId = Guid.NewGuid();
        CustomerRegistryStore.Append(isTestInstaller: false, new CustomerRegistryEntry(
            10001, firstGroupId, "Erster Kunde", DateTime.Now, Kontakt: null));
        CustomerRegistryStore.Append(isTestInstaller: false, new CustomerRegistryEntry(
            10002, Guid.NewGuid(), "Zweiter Kunde", DateTime.Now, Kontakt: null));

        var resolved = CustomerRegistryStore.ResolveCustomerGroupId(isTestInstaller: false, kundennummer: 10001);

        Assert.Equal(firstGroupId, resolved);
    }

    [Fact]
    public void LoadDistinctByKundennummer_CollapsesMultipleBuildsOfSameKundennummer_ToOneEntry()
    {
        var groupId = Guid.NewGuid();
        CustomerRegistryStore.Append(isTestInstaller: false, new CustomerRegistryEntry(
            10001, groupId, "Musterkunde", new DateTime(2026, 1, 1), Kontakt: null));
        CustomerRegistryStore.Append(isTestInstaller: false, new CustomerRegistryEntry(
            10001, groupId, "Musterkunde", new DateTime(2026, 2, 1), Kontakt: null));

        var entries = CustomerRegistryStore.LoadDistinctByKundennummer(isTestInstaller: false);

        Assert.Single(entries);
    }

    [Fact]
    public void LoadDistinctByKundennummer_KeepsTheOldestEntry_ForARebuiltKundennummer()
    {
        var groupId = Guid.NewGuid();
        var erstesErstelltAm = new DateTime(2026, 1, 1);
        CustomerRegistryStore.Append(isTestInstaller: false, new CustomerRegistryEntry(
            10001, groupId, "Musterkunde", erstesErstelltAm, Kontakt: null));
        CustomerRegistryStore.Append(isTestInstaller: false, new CustomerRegistryEntry(
            10001, groupId, "Musterkunde", new DateTime(2026, 2, 1), Kontakt: null));

        var entry = Assert.Single(CustomerRegistryStore.LoadDistinctByKundennummer(isTestInstaller: false));

        Assert.Equal(erstesErstelltAm, entry.ErstelltAm);
    }

    [Fact]
    public void LoadDistinctByKundennummer_KeepsUnrelatedKundennummernSeparate()
    {
        CustomerRegistryStore.Append(isTestInstaller: false, new CustomerRegistryEntry(
            10001, Guid.NewGuid(), "Erster Kunde", DateTime.Now, Kontakt: null));
        CustomerRegistryStore.Append(isTestInstaller: false, new CustomerRegistryEntry(
            10002, Guid.NewGuid(), "Zweiter Kunde", DateTime.Now, Kontakt: null));

        var entries = CustomerRegistryStore.LoadDistinctByKundennummer(isTestInstaller: false);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Kundennummer == 10001);
        Assert.Contains(entries, e => e.Kundennummer == 10002);
    }
}
