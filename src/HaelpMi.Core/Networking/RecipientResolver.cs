using HaelpMi.Core.Models;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Resolves an <see cref="AlarmProfile"/>'s asymmetric, possibly group- or room-based
/// recipient assignments (Teil 2, Abschnitt 4) down to a concrete device list for one
/// specific sender at trigger time. A <see cref="DeviceGroup"/> reference is resolved via
/// its direct <see cref="DeviceGroup.DeviceIds"/> membership - no indirection anymore (war
/// früher über Kreis-Mitgliedschaft, siehe EditScope.cs für den Hintergrund). Ein Room-Ref
/// (<see cref="EntityRef.RoomId"/>) wird bei jedem Alarm neu anhand der aktuellen
/// Raumnummer jedes Geräts aufgelöst - wie bei Gruppen zieht ein Geräteumzug in einen
/// anderen Raum automatisch nach, ohne dass die Zuordnung angefasst werden muss.
/// </summary>
public static class RecipientResolver
{
    public static IReadOnlyList<DeviceEntry> ResolveRecipientsForSender(
        AlarmProfile profile,
        Guid senderDeviceId,
        string senderRoomNumber,
        IReadOnlyList<DeviceEntry> allDevices,
        IReadOnlyList<DeviceGroup> groups)
    {
        var recipientDeviceIds = new HashSet<Guid>();

        foreach (var row in profile.RecipientAssignments)
        {
            if (!SenderMatches(row.Sender, senderDeviceId, senderRoomNumber, groups))
            {
                continue;
            }

            foreach (var recipientRef in row.Recipients)
            {
                foreach (var deviceId in ResolveEntityToDeviceIds(recipientRef, allDevices, groups))
                {
                    recipientDeviceIds.Add(deviceId);
                }
            }
        }

        return allDevices.Where(d => recipientDeviceIds.Contains(d.DeviceId)).ToList();
    }

    /// <summary>
    /// Distinct-Geräte-Anzahl für eine Empfänger-Liste (Admin-Dashboard-Anzeige, z. B. der
    /// Zähler-Badge im Sender-Auswahl - Nutzerwunsch 04.08.2026: "Gruppen werden nicht als
    /// 1 gezählt sondern als die Anzahl der hinterliegenden Empfänger, nicht doppelt
    /// gezählt, falls diese als Raum oder individuell auch angeklickt sind"). Nutzt
    /// dieselbe Auflösung wie beim tatsächlichen Alarmversand, daher deckungsgleich mit dem,
    /// was am Ende wirklich benachrichtigt wird.
    /// </summary>
    public static int CountDistinctRecipientDevices(IReadOnlyList<EntityRef> recipients, IReadOnlyList<DeviceEntry> allDevices, IReadOnlyList<DeviceGroup> groups)
    {
        var deviceIds = new HashSet<Guid>();
        foreach (var recipientRef in recipients)
        {
            foreach (var deviceId in ResolveEntityToDeviceIds(recipientRef, allDevices, groups))
            {
                deviceIds.Add(deviceId);
            }
        }
        return deviceIds.Count;
    }

    private static bool SenderMatches(EntityRef senderRef, Guid myDeviceId, string myRoomNumber, IReadOnlyList<DeviceGroup> groups)
    {
        switch (senderRef.Kind)
        {
            case EntityKind.Device:
                return senderRef.Id == myDeviceId;
            case EntityKind.Room:
                return senderRef.Id == EntityRef.RoomId(myRoomNumber);
            default:
                var group = groups.FirstOrDefault(g => g.Id == senderRef.Id);
                return group is not null && group.DeviceIds.Contains(myDeviceId);
        }
    }

    private static IEnumerable<Guid> ResolveEntityToDeviceIds(EntityRef entityRef, IReadOnlyList<DeviceEntry> allDevices, IReadOnlyList<DeviceGroup> groups)
    {
        switch (entityRef.Kind)
        {
            case EntityKind.Device:
                yield return entityRef.Id;
                yield break;
            case EntityKind.Room:
                foreach (var device in allDevices.Where(d => EntityRef.RoomId(d.RoomNumber) == entityRef.Id))
                {
                    yield return device.DeviceId;
                }
                yield break;
            default:
                var group = groups.FirstOrDefault(g => g.Id == entityRef.Id);
                if (group is null)
                {
                    yield break;
                }

                foreach (var deviceId in group.DeviceIds)
                {
                    yield return deviceId;
                }
                yield break;
        }
    }
}
