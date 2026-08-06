namespace HaelpMi.Core.Models;

/// <summary>
/// One configured alarm: name, text, hotkey, and the asymmetric per-sender recipient
/// mapping (Pflichtenheft Teil 2, Abschnitt 4: "Mehrere Alarm-Profile parallel möglich,
/// z. B. 'Notfall Nachbar', 'Notfall alle', jedes Profil mit eigenem Hotkey und eigenem
/// Sender-/Empfängerkreis").
///
/// Renamed from the Phase-1 "AlarmMessageConfig" now that it is genuinely a list with
/// more than one entry in normal use - this is exactly the Phase-1 extension point
/// (Pflichtenheft Phase 1, 5.9/FR-8) paying off: <see cref="OwnSettings.AlarmProfiles"/>
/// was already a list from day one, so supporting several profiles needed no data-model
/// change, only UI and hotkey registration looping over more than index 0.
/// </summary>
public sealed class AlarmProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name for the Admin-Dashboard, e.g. "Notfall Nachbar" - not shown to recipients.</summary>
    public string Name { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>Null means "not yet configured" - allowed per FR-2 (Tastenkürzel optional/überspringbar).</summary>
    public HotkeyDefinition? Hotkey { get; set; }

    /// <summary>
    /// "Schwellwert X" (Abschnitt 4/8): how many "bin unterwegs" responses a recipient's
    /// alarm window needs to see before it becomes closable. Per-profile, not global -
    /// an Admin can require more confirmations for a high-stakes profile than a routine
    /// one. Must be at least 1 (an alarm nobody can ever dismiss is a bug, not a feature).
    /// </summary>
    public int ResponseThreshold { get; set; } = 1;

    /// <summary>
    /// Per-sender recipient circles for this profile (Abschnitt 4). Resolved by the
    /// Admin-Dashboard; a User-role device only ever has the one row for itself as
    /// sender, since it cannot edit this at all (5. "kein Admin-Konto" per device -
    /// only Admin devices open the dashboard that writes this).
    /// </summary>
    public List<RecipientAssignment> RecipientAssignments { get; set; } = new();
}
