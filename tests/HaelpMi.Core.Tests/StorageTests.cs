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
    public void SettingsStore_Load_StampsFirstSeenUtc_WhenMissing()
    {
        // Issue #59/#60: der Installer setzt FirstSeenUtc nie (settings.json entsteht als
        // reines Pascal-Script) - Load() muss die Lücke selbst schließen, statt dauerhaft
        // bei default(DateTimeOffset) zu bleiben (das würde jedes so betroffene Gerät für
        // immer als "ältestes" einstufen).
        using var scope = new TestAppDataScope();
        var store = new SettingsStore();
        store.Save(new OwnSettings { DeviceId = Guid.NewGuid() });

        var before = DateTimeOffset.UtcNow;
        var loaded = store.Load();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(loaded.FirstSeenUtc, before, after);
        Assert.Equal(loaded.FirstSeenUtc, store.Load().FirstSeenUtc); // wurde tatsächlich zurückgeschrieben, nicht nur im Rückgabewert gesetzt
    }

    [Fact]
    public void SettingsStore_Load_KeepsExistingFirstSeenUtc()
    {
        using var scope = new TestAppDataScope();
        var store = new SettingsStore();
        var firstSeen = DateTimeOffset.UtcNow.AddDays(-90);
        store.Save(new OwnSettings { DeviceId = Guid.NewGuid(), FirstSeenUtc = firstSeen });

        Assert.Equal(firstSeen, store.Load().FirstSeenUtc);
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
        new(computerName, user, roomName, roomNumber, Role.User, false, ip, AppConstants.AlarmTcpPort, null);

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
    public void Upsert_NewDevice_FallsBackToLocalReceiveTime_WhenNoFirstSeenUtcReported()
    {
        var devices = new List<DeviceEntry>();
        var seenAt = DateTimeOffset.UtcNow;

        DeviceStore.Upsert(devices, Guid.NewGuid(), MakeInfo("PC-217", "Herr Novak", "Zimmer", "108", "192.168.1.50"), seenAt);

        Assert.Equal(seenAt, devices[0].FirstSeenUtc);
    }

    [Fact]
    public void Upsert_NewDevice_UsesReportedFirstSeenUtc_WhenGiven()
    {
        var devices = new List<DeviceEntry>();
        var reportedFirstSeen = DateTimeOffset.UtcNow.AddDays(-30);
        var info = new DeviceUpsertInfo("PC-217", "Herr Novak", "Zimmer", "108", Role.User, false, "192.168.1.50", AppConstants.AlarmTcpPort, reportedFirstSeen);

        DeviceStore.Upsert(devices, Guid.NewGuid(), info, DateTimeOffset.UtcNow);

        Assert.Equal(reportedFirstSeen, devices[0].FirstSeenUtc);
    }

    [Fact]
    public void Upsert_ExistingDevice_CorrectsFirstSeenUtc_OnlyDownward()
    {
        var deviceId = Guid.NewGuid();
        var originalFirstSeen = DateTimeOffset.UtcNow.AddDays(-10);
        var devices = new List<DeviceEntry> { new() { DeviceId = deviceId, FirstSeenUtc = originalFirstSeen } };

        // Ein späterer (unplausibler, "neuerer") gemeldeter Wert darf FirstSeenUtc nie
        // erhöhen - Fairness-Grundlage für LicenseLimitEvaluator (Issue #59/#60): ein
        // länger laufendes Gerät darf durch einen erneuten Kontakt nie "jünger" werden.
        var laterInfo = new DeviceUpsertInfo("PC", "U", "R", "1", Role.User, false, "1.2.3.4", AppConstants.AlarmTcpPort, DateTimeOffset.UtcNow);
        DeviceStore.Upsert(devices, deviceId, laterInfo, DateTimeOffset.UtcNow);
        Assert.Equal(originalFirstSeen, devices[0].FirstSeenUtc);

        // Ein per Gossip nachgelieferter, tatsächlich früherer Zeitpunkt (Drittwissen) DARF
        // die bisherige Schätzung nach unten korrigieren.
        var earlierFirstSeen = originalFirstSeen.AddDays(-5);
        var earlierInfo = new DeviceUpsertInfo("PC", "U", "R", "1", Role.User, false, "1.2.3.4", AppConstants.AlarmTcpPort, earlierFirstSeen);
        DeviceStore.Upsert(devices, deviceId, earlierInfo, DateTimeOffset.UtcNow);
        Assert.Equal(earlierFirstSeen, devices[0].FirstSeenUtc);
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

    // --- Issue #61: LicenseOverride-Gossip-Merge ("neuester Zeitstempel gewinnt") ---

    [Fact]
    public void Upsert_SelfReport_NeverTouchesExistingLicenseOverride()
    {
        var deviceId = Guid.NewGuid();
        var setAt = DateTimeOffset.UtcNow;
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = deviceId, ComputerName = "PC", LicenseOverride = LicenseOverride.ForceDisabled, LicenseOverrideSetAtUtc = setAt },
        };

        // Ein Selbstbericht (Override == null, wie bei einem direkten Boot-Call-Announce) darf
        // eine bereits bekannte Fremdmeinung nie überschreiben - ein Gerät kennt seine eigene
        // Override-Entscheidung nicht.
        DeviceStore.Upsert(devices, deviceId, MakeInfo("PC", "U", "R", "1", "1.2.3.4"), DateTimeOffset.UtcNow);

        Assert.Equal(LicenseOverride.ForceDisabled, devices[0].LicenseOverride);
        Assert.Equal(setAt, devices[0].LicenseOverrideSetAtUtc);
    }

    [Fact]
    public void Upsert_GossipOverride_AppliesWhenNewerThanLocal()
    {
        var deviceId = Guid.NewGuid();
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = deviceId, LicenseOverride = LicenseOverride.None, LicenseOverrideSetAtUtc = null },
        };

        var newerInfo = new DeviceUpsertInfo("PC", "U", "R", "1", Role.User, false, "1.2.3.4", AppConstants.AlarmTcpPort,
            null, LicenseOverride.ForceDisabled, DateTimeOffset.UtcNow);
        DeviceStore.Upsert(devices, deviceId, newerInfo, DateTimeOffset.UtcNow);

        Assert.Equal(LicenseOverride.ForceDisabled, devices[0].LicenseOverride);
    }

    [Fact]
    public void Upsert_GossipOverride_IgnoredWhenOlderThanLocal()
    {
        var deviceId = Guid.NewGuid();
        var localSetAt = DateTimeOffset.UtcNow;
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = deviceId, LicenseOverride = LicenseOverride.ForceEnabled, LicenseOverrideSetAtUtc = localSetAt },
        };

        var staleInfo = new DeviceUpsertInfo("PC", "U", "R", "1", Role.User, false, "1.2.3.4", AppConstants.AlarmTcpPort,
            null, LicenseOverride.ForceDisabled, localSetAt.AddMinutes(-5));
        DeviceStore.Upsert(devices, deviceId, staleInfo, DateTimeOffset.UtcNow);

        Assert.Equal(LicenseOverride.ForceEnabled, devices[0].LicenseOverride); // ein älterer Gossip-Stand darf eine neuere lokale Entscheidung nicht zurückdrehen
    }

    [Fact]
    public void SetLicenseOverride_SetsValueAndTimestamp_UnconditionallyLocal()
    {
        var deviceId = Guid.NewGuid();
        var devices = new List<DeviceEntry> { new() { DeviceId = deviceId } };
        var setAt = DateTimeOffset.UtcNow;

        DeviceStore.SetLicenseOverride(devices, deviceId, LicenseOverride.ForceDisabled, setAt);

        Assert.Equal(LicenseOverride.ForceDisabled, devices[0].LicenseOverride);
        Assert.Equal(setAt, devices[0].LicenseOverrideSetAtUtc);
    }

    [Fact]
    public void Remove_DeletesOnlyTheMatchingDevice()
    {
        var keepId = Guid.NewGuid();
        var removeId = Guid.NewGuid();
        var devices = new List<DeviceEntry> { new() { DeviceId = keepId }, new() { DeviceId = removeId } };

        DeviceStore.Remove(devices, removeId);

        Assert.Single(devices);
        Assert.Equal(keepId, devices[0].DeviceId);
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
}
