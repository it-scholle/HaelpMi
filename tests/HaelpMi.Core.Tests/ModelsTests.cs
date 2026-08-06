using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using Xunit;

namespace HaelpMi.Core.Tests;

public class ModelsTests
{
    [Fact]
    public void HotkeyDefinition_Format_CombinesModifiersAndKeyName()
    {
        var hotkey = new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Alt, (int)'H');

        Assert.Equal("Strg+Alt+H", hotkey.Format());
    }

    [Fact]
    public void HotkeyDefinition_Format_FallsBackToVirtualKeyCode_ForUnknownKeys()
    {
        var hotkey = new HotkeyDefinition(HotkeyModifiers.None, 0xFE);

        Assert.Equal("VK 0xFE", hotkey.Format());
    }

    [Fact]
    public void IncomingSoundCatalog_Resolve_FallsBackToFirstOption_ForUnknownId()
    {
        var resolved = IncomingSoundCatalog.Resolve("does-not-exist");

        Assert.Equal(IncomingSoundCatalog.Options[0].Id, resolved.Id);
    }

    // --- Teil 2, Abschnitt 4: RecipientResolver - Kernfunktion für die asymmetrische
    // Empfänger-Zuordnung, die Task #20 (Drag-and-Drop) letztlich verwaltet. ---

    [Fact]
    public void RecipientResolver_ResolvesDirectDeviceSender_ToItsDirectDeviceRecipient()
    {
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Device, senderId),
                    Recipients = { new EntityRef(EntityKind.Device, recipientId) },
                },
            },
        };
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = senderId },
            new() { DeviceId = recipientId },
        };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, groups: new List<DeviceGroup>());

        Assert.Single(result);
        Assert.Equal(recipientId, result[0].DeviceId);
    }

    [Fact]
    public void RecipientResolver_ResolvesGroupSender_ViaDirectDeviceMembership()
    {
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var group = new DeviceGroup { DeviceIds = { senderId } };
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Group, group.Id),
                    Recipients = { new EntityRef(EntityKind.Device, recipientId) },
                },
            },
        };
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = senderId },
            new() { DeviceId = recipientId },
        };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, new List<DeviceGroup> { group });

        Assert.Single(result);
        Assert.Equal(recipientId, result[0].DeviceId);
    }

    [Fact]
    public void RecipientResolver_ResolvesGroupRecipient_ToAllMemberDevices()
    {
        var senderId = Guid.NewGuid();
        var deviceInGroup1 = Guid.NewGuid();
        var deviceInGroup2 = Guid.NewGuid();
        var deviceNotInGroup = Guid.NewGuid();
        var group = new DeviceGroup { DeviceIds = { deviceInGroup1, deviceInGroup2 } };
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Device, senderId),
                    Recipients = { new EntityRef(EntityKind.Group, group.Id) },
                },
            },
        };
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = senderId },
            new() { DeviceId = deviceInGroup1 },
            new() { DeviceId = deviceInGroup2 },
            new() { DeviceId = deviceNotInGroup },
        };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, new List<DeviceGroup> { group });

        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.DeviceId == deviceInGroup1);
        Assert.Contains(result, d => d.DeviceId == deviceInGroup2);
    }

    [Fact]
    public void RecipientResolver_NonMatchingSender_ResolvesToNoRecipients()
    {
        var senderId = Guid.NewGuid();
        var otherSenderId = Guid.NewGuid();
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Device, otherSenderId),
                    Recipients = { new EntityRef(EntityKind.Device, Guid.NewGuid()) },
                },
            },
        };
        var devices = new List<DeviceEntry> { new() { DeviceId = senderId } };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, new List<DeviceGroup>());

        Assert.Empty(result);
    }

    // --- Nutzer-Klarstellung 04.08.2026: Raum als eigener, live aufgelöster Empfänger-/
    // Sender-Typ (EntityRef.RoomId) - kein gespeichertes Objekt, ergibt sich aus der
    // aktuellen Raumnummer jedes Geräts. ---

    [Fact]
    public void RecipientResolver_ResolvesRoomRecipient_ToAllDevicesCurrentlySharingThatRoomNumber()
    {
        var senderId = Guid.NewGuid();
        var deviceInRoom1 = Guid.NewGuid();
        var deviceInRoom2 = Guid.NewGuid();
        var deviceInOtherRoom = Guid.NewGuid();
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Device, senderId),
                    Recipients = { EntityRef.ForRoom("214") },
                },
            },
        };
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = senderId, RoomNumber = "1" },
            new() { DeviceId = deviceInRoom1, RoomNumber = "214" },
            new() { DeviceId = deviceInRoom2, RoomNumber = "214" },
            new() { DeviceId = deviceInOtherRoom, RoomNumber = "215" },
        };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, new List<DeviceGroup>());

        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.DeviceId == deviceInRoom1);
        Assert.Contains(result, d => d.DeviceId == deviceInRoom2);
    }

    [Fact]
    public void RecipientResolver_ResolvesRoomSender_ViaTheSendingDevicesOwnCurrentRoomNumber()
    {
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = EntityRef.ForRoom("214"),
                    Recipients = { new EntityRef(EntityKind.Device, recipientId) },
                },
            },
        };
        // Der Sender selbst steht (wie im echten Betrieb) nicht in seiner eigenen
        // Geräteliste - RoomId-Abgleich läuft daher über den separaten senderRoomNumber-
        // Parameter, nicht über eine Suche in allDevices.
        var devices = new List<DeviceEntry> { new() { DeviceId = recipientId } };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "214", devices, new List<DeviceGroup>());

        Assert.Single(result);
        Assert.Equal(recipientId, result[0].DeviceId);
    }

    // --- Nutzerwunsch 04.08.2026: Zähler-Badge in der Sender-Auswahl des Admin-
    // Dashboards - "Gruppen werden nicht als 1 gezählt sondern als die Anzahl der
    // hinterliegenden Empfänger, nicht doppelt gezählt, falls diese als Raum oder
    // individuell auch angeklickt sind". ---

    [Fact]
    public void CountDistinctRecipientDevices_CountsGroupMembers_NotTheGroupItself()
    {
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var group = new DeviceGroup { DeviceIds = { deviceA, deviceB } };
        var devices = new List<DeviceEntry> { new() { DeviceId = deviceA }, new() { DeviceId = deviceB } };

        var count = RecipientResolver.CountDistinctRecipientDevices(
            new List<EntityRef> { new(EntityKind.Group, group.Id) }, devices, new List<DeviceGroup> { group });

        Assert.Equal(2, count);
    }

    [Fact]
    public void CountDistinctRecipientDevices_DoesNotDoubleCount_WhenSameDeviceReachableViaMultipleRefs()
    {
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        var group = new DeviceGroup { DeviceIds = { deviceA, deviceB } };
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = deviceA, RoomNumber = "214" },
            new() { DeviceId = deviceB, RoomNumber = "214" },
        };

        // deviceA ist gleichzeitig: individuell ausgewählt, über den Raum "214" erreichbar,
        // UND Mitglied der Gruppe - trotzdem darf er im Ergebnis nur einmal zählen.
        var count = RecipientResolver.CountDistinctRecipientDevices(
            new List<EntityRef>
            {
                new(EntityKind.Device, deviceA),
                EntityRef.ForRoom("214"),
                new(EntityKind.Group, group.Id),
            },
            devices, new List<DeviceGroup> { group });

        Assert.Equal(2, count); // deviceA + deviceB, nicht mehr
    }
}
