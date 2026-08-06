namespace HaelpMi.Core.Models;

/// <summary>
/// One entry in the config change history, scoped per edited record (Pflichtenheft
/// Teil 2, Abschnitt 10: "Änderungshistorie ..., letzte n Einträge, mit Undo").
/// <see cref="ScopeKind"/>/<see cref="ScopeId"/> identify exactly which Gruppe or
/// Alarm-Profil this entry belongs to - war früher pro Kreis, siehe EditScope.cs für den
/// Hintergrund. Contains clear names (who changed what) - CLAUDE.md v2 flags this as
/// needing Personalrat sign-off before wider rollout, same caveat as Phase 1's response
/// log; not a decision this codebase makes on its own, just keeps the data minimal
/// (field/old/new value, no free-text).
/// </summary>
public sealed class ConfigHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required EditScopeKind ScopeKind { get; set; }

    public required Guid ScopeId { get; set; }

    public DateTimeOffset ChangedAtUtc { get; set; }

    /// <summary>Clear name of who made the change (device's configured User) - see class remarks.</summary>
    public string ChangedByUser { get; set; } = string.Empty;

    public required Guid ChangedByDeviceId { get; set; }

    /// <summary>Dotted path identifying the changed field, e.g. "Gruppe.Name" or "AlarmProfil.Text".</summary>
    public required string FieldPath { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    /// <summary>The config version this change produced - what a hot-reload broadcast for this change carries.</summary>
    public int ResultingConfigVersion { get; set; }

    /// <summary>
    /// Full <see cref="SharedConfig"/> as it was *before* this change, serialized. Undo
    /// restores this wholesale rather than trying to reverse-apply <see cref="FieldPath"/>
    /// generically - simpler and correct regardless of how deep or structural a given
    /// change was (e.g. a whole Empfängerkreis-Zuordnung, not just one scalar field).
    /// </summary>
    public required string PreviousConfigSnapshotJson { get; set; }
}
