using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using Xunit;

namespace HaelpMi.Core.Tests;

public class ModelsTests
{
    // Issue #92: AlarmDispatcherReadyTimeout begrenzt ein aus dem TCP-Empfangsthread
    // ausgelöstes Dispatcher.Invoke in AlarmFlowCoordinator - muss kleiner als
    // AlarmAckTimeout bleiben, sonst ist der Ack/Status-Relay beim Sender ohnehin schon
    // abgelaufen, bevor die Invoke-Sperre überhaupt nachgibt (siehe Kommentar an der
    // Konstante selbst für die volle Begründung).
    [Fact]
    public void AlarmDispatcherReadyTimeout_IsShorterThan_AlarmAckTimeout()
    {
        Assert.True(AppConstants.AlarmDispatcherReadyTimeout < AppConstants.AlarmAckTimeout);
    }

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

    // --- Bugfix 08.08.2026 (TEST-STRATEGY.md E11, "Meine Alarme" zeigte kein Profil mehr,
    // sobald das eigene Gerät selbst zu seinen aufgelösten Empfängern zählte): der Fehler
    // saß in ConfigWindow.RebuildMyAlarms (HaelpMi.UI, WPF) - LoadDevices() lieferte dort
    // nie das eigene Gerät (wie im echten Betrieb, siehe Kommentar bei
    // RecipientResolver_ResolvesRoomSender_ViaTheSendingDevicesOwnCurrentRoomNumber oben),
    // wodurch ResolveRecipientsForSender ein an sich korrekt aufgelöstes eigenes Gerät im
    // letzten Schritt (Abgleich gegen allDevices) wieder herausfilterte. Der WPF-Layer
    // selbst ist laut TEST-STRATEGY.md bewusst (noch) nicht coded getestet (FlaUI erst ab
    // 1.0/Layout-Stabilität) - diese drei Fälle sichern stattdessen die Resolver-Annahme
    // ab, auf der der Fix (ConfigWindowContext.LoadOwnDevice + `.Append(ownDevice)`) beruht:
    // sobald das eigene Gerät in allDevices steht, muss es korrekt als Empfänger
    // durchgereicht werden, egal auf welchem Weg (direkt/Gruppe/Raum). ---

    [Fact]
    public void RecipientResolver_IncludesSenderDevice_WhenSenderIsItsOwnDirectRecipient()
    {
        var senderId = Guid.NewGuid();
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Device, senderId),
                    Recipients = { new EntityRef(EntityKind.Device, senderId) },
                },
            },
        };
        // Anders als sonst in dieser Testklasse: das eigene Gerät ist HIER in allDevices
        // enthalten - genau das, was LoadOwnDevice seit dem Fix sicherstellt.
        var devices = new List<DeviceEntry> { new() { DeviceId = senderId } };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, new List<DeviceGroup>());

        Assert.Single(result);
        Assert.Equal(senderId, result[0].DeviceId);
    }

    [Fact]
    public void RecipientResolver_IncludesSenderDevice_WhenSenderIsRecipientViaItsOwnGroup()
    {
        var senderId = Guid.NewGuid();
        var group = new DeviceGroup { DeviceIds = { senderId } };
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
        var devices = new List<DeviceEntry> { new() { DeviceId = senderId } };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, new List<DeviceGroup> { group });

        Assert.Single(result);
        Assert.Equal(senderId, result[0].DeviceId);
    }

    [Fact]
    public void RecipientResolver_IncludesSenderDevice_WhenSenderIsRecipientViaItsOwnRoom()
    {
        var senderId = Guid.NewGuid();
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
        var devices = new List<DeviceEntry> { new() { DeviceId = senderId, RoomNumber = "214" } };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "214", devices, new List<DeviceGroup>());

        Assert.Single(result);
        Assert.Equal(senderId, result[0].DeviceId);
    }

    // --- Fixes #5 ("Ruft um Hilfe" ging fälschlich auch beim Sender selbst auf, sobald
    // dessen eigenes Gerät im aufgelösten Empfängerkreis steckte): excludeSender:true ist
    // der Gegenpol zu den drei IncludesSenderDevice-Tests oben - gleiche drei Auflösungswege
    // (direkt/Gruppe/Raum), aber mit dem beim tatsächlichen Alarmversand (AlarmFlowCoordinator)
    // gesetzten Flag, das den Sender unabhängig davon ausschließt, ob er in allDevices steht. ---

    [Fact]
    public void RecipientResolver_ExcludesSenderDevice_WhenExcludeSenderIsSet_ViaDirectRecipient()
    {
        var senderId = Guid.NewGuid();
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Device, senderId),
                    Recipients = { new EntityRef(EntityKind.Device, senderId) },
                },
            },
        };
        var devices = new List<DeviceEntry> { new() { DeviceId = senderId } };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, new List<DeviceGroup>(), excludeSender: true);

        Assert.Empty(result);
    }

    [Fact]
    public void RecipientResolver_ExcludesSenderDevice_WhenExcludeSenderIsSet_ViaItsOwnGroup()
    {
        var senderId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var group = new DeviceGroup { DeviceIds = { senderId, peerId } };
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
        var devices = new List<DeviceEntry> { new() { DeviceId = senderId }, new() { DeviceId = peerId } };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, new List<DeviceGroup> { group }, excludeSender: true);

        Assert.Single(result);
        Assert.Equal(peerId, result[0].DeviceId);
    }

    [Fact]
    public void RecipientResolver_ExcludesSenderDevice_WhenExcludeSenderIsSet_ViaItsOwnRoom()
    {
        var senderId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
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
            new() { DeviceId = senderId, RoomNumber = "214" },
            new() { DeviceId = peerId, RoomNumber = "214" },
        };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "214", devices, new List<DeviceGroup>(), excludeSender: true);

        Assert.Single(result);
        Assert.Equal(peerId, result[0].DeviceId);
    }

    [Fact]
    public void RecipientResolver_ExcludesSenderDevice_WhenExcludeSenderIsSet_ViaAllDevicesGroup()
    {
        var senderId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Device, senderId),
                    Recipients = { new EntityRef(EntityKind.Group, AppConstants.AllDevicesGroupId) },
                },
            },
        };
        // Anders als beim bisherigen "Alle"-Test bewusst MIT Sender in allDevices - der
        // ursprüngliche Bug bestand ja gerade darin, dass sich das nur zufällig nie so ergab.
        var devices = new List<DeviceEntry> { new() { DeviceId = senderId }, new() { DeviceId = peerId } };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, new List<DeviceGroup>(), excludeSender: true);

        Assert.Single(result);
        Assert.Equal(peerId, result[0].DeviceId);
    }

    // --- Nutzerwunsch 09.08.2026: eingebaute "Alle"-Gruppe (AppConstants.AllDevicesGroupId) -
    // Mitgliedschaft kommt live aus den bekannten Geräten, nicht aus DeviceIds/Config-Sync, und
    // funktioniert daher unabhängig davon, ob sie überhaupt in der übergebenen groups-Liste
    // steht (so, wie ein frisches, noch nie synchronisiertes Gerät sie lokal kennt). ---

    [Fact]
    public void RecipientResolver_ResolvesAllDevicesGroupRecipient_ToEveryCurrentlyKnownDevice()
    {
        var senderId = Guid.NewGuid();
        var peer1 = Guid.NewGuid();
        var peer2 = Guid.NewGuid();
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Device, senderId),
                    Recipients = { new EntityRef(EntityKind.Group, AppConstants.AllDevicesGroupId) },
                },
            },
        };
        // Nur Peers (Sender selbst nicht enthalten) - so wie _deviceStore.Load() zur
        // Trigger-Zeit tatsächlich befüllt ist (der Sender trägt sich dort nie selbst ein).
        var devices = new List<DeviceEntry> { new() { DeviceId = peer1 }, new() { DeviceId = peer2 } };

        // Bewusst eine LEERE groups-Liste: "Alle" muss auch auflösen, wenn sie (noch) gar
        // nicht in der lokal geladenen SharedConfig steht.
        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, groups: new List<DeviceGroup>());

        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.DeviceId == peer1);
        Assert.Contains(result, d => d.DeviceId == peer2);
    }

    [Fact]
    public void RecipientResolver_ResolvesAllDevicesGroupSender_RegardlessOfGroupsList()
    {
        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var profile = new AlarmProfile
        {
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Group, AppConstants.AllDevicesGroupId),
                    Recipients = { new EntityRef(EntityKind.Device, recipientId) },
                },
            },
        };
        var devices = new List<DeviceEntry> { new() { DeviceId = senderId }, new() { DeviceId = recipientId } };

        var result = RecipientResolver.ResolveRecipientsForSender(profile, senderId, "1", devices, groups: new List<DeviceGroup>());

        Assert.Single(result);
        Assert.Equal(recipientId, result[0].DeviceId);
    }

    [Fact]
    public void DeviceGroup_IsBuiltInAllDevicesGroup_TrueOnlyForTheReservedId()
    {
        Assert.True(new DeviceGroup { Id = AppConstants.AllDevicesGroupId }.IsBuiltInAllDevicesGroup);
        Assert.False(new DeviceGroup().IsBuiltInAllDevicesGroup); // random Guid.NewGuid() default
    }
}
