namespace HaelpMi.Core.Models;

/// <summary>
/// One row of the asymmetric per-sender recipient mapping (Pflichtenheft Teil 2,
/// Abschnitt 4: "Empfängerkreis pro Sender ist asymmetrisch konfigurierbar: pro
/// Hotkey/Profil und pro Sender ... ein individueller Empfängerkreis"). Lives inside an
/// <see cref="AlarmProfile"/> - the same physical device can be a sender in one row and
/// a recipient in a completely different row of the same or another profile.
/// </summary>
public sealed class RecipientAssignment
{
    public required EntityRef Sender { get; set; }

    public List<EntityRef> Recipients { get; set; } = new();
}
