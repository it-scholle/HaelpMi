using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Covers Geräte-Kennung-Abgleich, "Neu"-Zustand, und die Teil-2-Ersteinrichtung-per-Installer-Semantik von SettingsStore.</summary>
public class StorageTests
{
    [Fact]
    public void SettingsStore_Load_Throws_WhenNoFileExistsYet()
    {
        // Teil 2: settings.json wird ausschließlich vom Installer geschrieben (FR-36 bis
        // FR-39) - fehlt sie, ist das eine unvollständige Installation, kein normaler
        // Ersteinrichtungs-Zustand mehr (siehe SettingsStore.cs-Kommentar).
        using var scope = new TestAppDataScope();
        var store = new SettingsStore();

        Assert.Throws<InvalidOperationException>(() => store.Load());
    }

    [Fact]
    public void SettingsStore_RoundTrips_OwnSettings()
    {
        using var scope = new TestAppDataScope();
        var store = new SettingsStore();

        var settings = new OwnSettings
        {
            DeviceId = Guid.NewGuid(),
            ComputerName = "Sachbearbeitung 3",
            RoomName = "Zimmer",
            RoomNumber = "214",
            IncomingSoundId = "ton-2",
            Role = Role.Admin,
            FirstRunCompleted = true,
        };
        store.Save(settings);

        var reloaded = store.Load();
        Assert.Equal(settings.DeviceId, reloaded.DeviceId);
        Assert.Equal("Sachbearbeitung 3", reloaded.ComputerName);
        Assert.Equal("Zimmer", reloaded.RoomName);
        Assert.Equal("214", reloaded.RoomNumber);
        Assert.Equal("ton-2", reloaded.IncomingSoundId);
        Assert.Equal(Role.Admin, reloaded.Role);
        Assert.True(reloaded.FirstRunCompleted);
    }

    [Fact]
    public void DeviceStore_RoundTrips_DeviceList()
    {
        using var scope = new TestAppDataScope();
        var store = new DeviceStore();

        var devices = new List<DeviceEntry>
        {
            new()
            {
                DeviceId = Guid.NewGuid(), ComputerName = "Empfang EG", RoomName = "Empfangshalle", RoomNumber = "0",
                User = "Poststelle", Favorite = true, Notified = true, Note = "immer besetzt", IsNew = false,
            },
        };
        store.Save(devices);

        var reloaded = store.Load();
        Assert.Single(reloaded);
        Assert.Equal("Empfang EG", reloaded[0].ComputerName);
        Assert.True(reloaded[0].Favorite);
        Assert.Equal("immer besetzt", reloaded[0].Note);
    }

    [Fact]
    public void DeviceStore_Load_ReturnsEmptyList_WhenNoFileExistsYet()
    {
        using var scope = new TestAppDataScope();
        var store = new DeviceStore();

        Assert.Empty(store.Load());
    }

    private static DeviceUpsertInfo MakeInfo(string computerName, string user, string roomName, string roomNumber, string ip) =>
        new(computerName, user, roomName, roomNumber, Role.User, false, ip, AppConstants.AlarmTcpPort);

    [Fact]
    public void Upsert_AddsNewDevice_MarkedAsNew()
    {
        var devices = new List<DeviceEntry>();
        var deviceId = Guid.NewGuid();

        DeviceStore.Upsert(devices, deviceId, MakeInfo("PC-217", "Herr Novak", "Zimmer", "108", "192.168.1.50"), DateTimeOffset.UtcNow);

        Assert.Single(devices);
        Assert.True(devices[0].IsNew); // FR-24: newly discovered devices start highlighted
        Assert.False(devices[0].Notified);
    }

    [Fact]
    public void Upsert_MatchesExistingDevice_ByDeviceId_NotByIp()
    {
        var deviceId = Guid.NewGuid();
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = deviceId, ComputerName = "Alt-Name", IpAddress = "192.168.1.10", Favorite = true, Notified = true, Note = "wichtig", IsNew = false },
        };

        // Same device, new IP (e.g. DHCP lease renewed) and an updated computer name.
        DeviceStore.Upsert(devices, deviceId, MakeInfo("Neuer Name", "Neuer Nutzer", "Neuer Raum", "1", "192.168.1.99"), DateTimeOffset.UtcNow);

        Assert.Single(devices); // no duplicate row was created (FR-22)
        var entry = devices[0];
        Assert.Equal("Neuer Name", entry.ComputerName);
        Assert.Equal("192.168.1.99", entry.IpAddress);
        // Local decisions are untouched by a re-announce.
        Assert.True(entry.Favorite);
        Assert.True(entry.Notified);
        Assert.Equal("wichtig", entry.Note);
        Assert.False(entry.IsNew);
    }

    [Fact]
    public void Upsert_OfDifferentDeviceId_AtSameIp_CreatesSeparateEntry()
    {
        // Two different devices could share an IP over time (address reassigned by DHCP);
        // the reconciliation key must stay the device id, never the IP (FR-3/FR-22).
        var devices = new List<DeviceEntry>();
        DeviceStore.Upsert(devices, Guid.NewGuid(), MakeInfo("PC-A", "A", "Raum A", "1", "192.168.1.20"), DateTimeOffset.UtcNow);
        DeviceStore.Upsert(devices, Guid.NewGuid(), MakeInfo("PC-B", "B", "Raum B", "2", "192.168.1.20"), DateTimeOffset.UtcNow);

        Assert.Equal(2, devices.Count);
    }

    [Fact]
    public void AcknowledgeNewState_ClearsIsNew()
    {
        var deviceId = Guid.NewGuid();
        var devices = new List<DeviceEntry> { new() { DeviceId = deviceId, IsNew = true } };

        DeviceStore.AcknowledgeNewState(devices, deviceId);

        Assert.False(devices[0].IsNew);
    }

    [Fact]
    public void AcknowledgeNewState_ThenReUpsert_DoesNotResetIsNew()
    {
        var deviceId = Guid.NewGuid();
        var devices = new List<DeviceEntry> { new() { DeviceId = deviceId, IsNew = true } };

        DeviceStore.AcknowledgeNewState(devices, deviceId);
        DeviceStore.Upsert(devices, deviceId, MakeInfo("Name", "User", "Room", "1", "192.168.1.30"), DateTimeOffset.UtcNow);

        Assert.False(devices[0].IsNew); // a later re-announce must not re-highlight an already-acknowledged device
    }

    [Fact]
    public void OrderForDisplay_PutsFavoritesFirst()
    {
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = Guid.NewGuid(), ComputerName = "B", Favorite = false },
            new() { DeviceId = Guid.NewGuid(), ComputerName = "A", Favorite = true },
            new() { DeviceId = Guid.NewGuid(), ComputerName = "C", Favorite = false },
        };

        var ordered = DeviceStore.OrderForDisplay(devices).ToList();

        Assert.Equal("A", ordered[0].ComputerName); // the only favorite (FR-19)
    }

    // --- Nutzerwunsch 09.08.2026: eingebaute "Alle"-Gruppe - jedes Gerät ergänzt sie lokal
    // beim Laden, ganz ohne Config-Sync/Admin, statt sie propagiert zu bekommen. ---

    [Fact]
    public void SharedConfigStore_LoadOrCreate_AddsAllDevicesGroup_OnAFreshNeverSavedConfig()
    {
        using var scope = new TestAppDataScope();
        var store = new SharedConfigStore();

        var config = store.LoadOrCreate();

        Assert.Contains(config.DeviceGroups, g => g.Id == AppConstants.AllDevicesGroupId && g.Name == "Alle");
    }

    [Fact]
    public void SharedConfigStore_LoadOrCreate_BackfillsAllDevicesGroup_OnAnOlderSavedConfigMissingIt()
    {
        using var scope = new TestAppDataScope();
        var store = new SharedConfigStore();

        // Simuliert ein bereits synchronisiertes Gerät, dessen gespeicherte SharedConfig aus
        // der Zeit vor der "Alle"-Gruppe stammt (kein Save() über LoadOrCreate benutzt).
        store.Save(new SharedConfig { DeviceGroups = { new DeviceGroup { Name = "Erdgeschoss" } } });

        var config = store.LoadOrCreate();

        Assert.Equal(2, config.DeviceGroups.Count);
        Assert.Contains(config.DeviceGroups, g => g.Id == AppConstants.AllDevicesGroupId);
        Assert.Contains(config.DeviceGroups, g => g.Name == "Erdgeschoss");
    }

    [Fact]
    public void SharedConfigStore_LoadOrCreate_DoesNotDuplicate_WhenAllDevicesGroupAlreadyPresent()
    {
        using var scope = new TestAppDataScope();
        var store = new SharedConfigStore();
        store.Save(new SharedConfig { DeviceGroups = { new DeviceGroup { Id = AppConstants.AllDevicesGroupId, Name = "Alle" } } });

        var config = store.LoadOrCreate();

        Assert.Single(config.DeviceGroups);
    }

    [Fact]
    public void SharedConfigStore_LoadOrCreate_MigratesLegacySingleBridgeSeedAddress_IntoNewList()
    {
        // Prio 0.1 (15.08.2026): altes Format vor v0.28.0 (cf6b252) von Hand geschrieben, da
        // SharedConfig das Feld "BridgeSeedAddress" (Singular) nicht mehr kennt und es daher
        // nicht mehr über store.Save() erzeugt werden kann.
        using var scope = new TestAppDataScope();
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SharedConfigFilePath)!);
        File.WriteAllText(AppPaths.SharedConfigFilePath, """{"ConfigVersion":1,"BridgeSeedAddress":"10.0.5.3"}""");
        var store = new SharedConfigStore();

        var config = store.LoadOrCreate();

        Assert.Equal(new[] { "10.0.5.3" }, config.BridgeSeedAddresses);
    }

    [Fact]
    public void SharedConfigStore_LoadOrCreate_DoesNotOverwriteBridgeSeedAddresses_WhenAlreadyPopulated()
    {
        // Der Admin hat seit dem Update auf v0.28.0 schon einmal im neuen Format gespeichert -
        // ein zufällig noch in der Datei stehender alter Schlüssel darf die neue Liste dann
        // nicht mehr anfassen.
        using var scope = new TestAppDataScope();
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SharedConfigFilePath)!);
        File.WriteAllText(AppPaths.SharedConfigFilePath, """{"ConfigVersion":2,"BridgeSeedAddress":"10.0.5.3","BridgeSeedAddresses":["10.0.9.9"]}""");
        var store = new SharedConfigStore();

        var config = store.LoadOrCreate();

        Assert.Equal(new[] { "10.0.9.9" }, config.BridgeSeedAddresses);
    }

    [Fact]
    public void SharedConfigStore_LoadOrCreate_LeavesBridgeSeedAddressesEmpty_WhenNoLegacyKeyPresent()
    {
        using var scope = new TestAppDataScope();
        var store = new SharedConfigStore();

        var config = store.LoadOrCreate();

        Assert.Empty(config.BridgeSeedAddresses);
    }
}
