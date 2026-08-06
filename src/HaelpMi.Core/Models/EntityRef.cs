using System.Security.Cryptography;
using System.Text;

namespace HaelpMi.Core.Models;

public enum EntityKind
{
    Device,
    Group,
    Room,
}

/// <summary>
/// A polymorphic reference to "either one device, one <see cref="DeviceGroup"/>, or every
/// device currently sharing one Raumnummer" - used wherever the Admin-Dashboard lets a
/// sender or recipient be picked (Pflichtenheft Teil 2, Abschnitt 4: "Empfängerkreis ...
/// auch gruppenübergreifend definierbar"; Nutzer-Klarstellung 04.08.2026: der eigentliche
/// eingerichtete User war zuvor nirgends direkt auswählbar, nur Kreis/Gruppe - Device-Refs
/// sind das jetzt im Dashboard als "Nutzer" beschriftet, kein neues Konzept). Resolving a
/// Group or Room reference down to its actual device set happens at send time (see
/// RecipientResolver), not when the reference is stored - so renaming/regrouping/moving a
/// device to a different room later doesn't require rewriting every assignment.
/// </summary>
public sealed record EntityRef(EntityKind Kind, Guid Id)
{
    /// <summary>
    /// Räume sind - anders als Geräte oder Gruppen - keine eigenständig gespeicherten
    /// Objekte mit einer echten Guid; "Raum 214" ergibt sich rein aus der Raumnummer, die
    /// mehrere Geräte gerade zufällig teilen. Für eine stabile, über Neustarts und Geräte
    /// hinweg identische EntityRef.Id wird die Raumnummer deterministisch auf eine Guid
    /// abgebildet (wie ein UUIDv5-Namensraum) - zwei Aufrufe mit derselben Raumnummer
    /// liefern immer dieselbe Id, ohne dass irgendwo eine Zuordnungstabelle gepflegt werden muss.
    /// </summary>
    public static Guid RoomId(string roomNumber)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("HaelpMi.Room:" + roomNumber));
        return new Guid(hash);
    }

    public static EntityRef ForRoom(string roomNumber) => new(EntityKind.Room, RoomId(roomNumber));
}
