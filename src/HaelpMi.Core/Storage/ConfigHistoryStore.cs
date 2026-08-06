using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>
/// Append-only (capped) config change history, one shared file covering all Gruppen/
/// Alarm-Profile - filtered by <see cref="ConfigHistoryEntry.ScopeKind"/>/
/// <see cref="ConfigHistoryEntry.ScopeId"/> at read time rather than split into
/// per-record files, since the Admin-Dashboard is the only reader and a single small
/// JSON file is simpler to keep consistent (Teil 2, Abschnitt 10: "letzte n Einträge" -
/// capped, not unbounded).
/// </summary>
public sealed class ConfigHistoryStore
{
    /// <summary>"letzte n Einträge" - kept generous since this is a small admin-facing log, not per-alarm data.</summary>
    public const int MaxEntriesPerScope = 200;

    public List<ConfigHistoryEntry> Load()
    {
        try
        {
            return JsonFileStore.Load<List<ConfigHistoryEntry>>(AppPaths.ConfigHistoryFilePath) ?? new List<ConfigHistoryEntry>();
        }
        catch (System.Text.Json.JsonException)
        {
            // Datei kann noch aus der Zeit vor der Kreis→Gruppe-Umstellung (04.08.2026)
            // stammen, wo ConfigHistoryEntry ein CircleId- statt ScopeKind/ScopeId-Feld
            // hatte - in der Alpha-Phase gibt es dafür bewusst keinen Migrationspfad, alte
            // Verlaufseinträge sind eine reine Anzeige-Historie, kein Betriebsdatenverlust.
            // Ohne diesen Fallback blockiert das JEDES Speichern (Load() läuft vor jedem
            // Publish, siehe ConfigSyncService.PublishAsync), nicht nur das Lesen der Historie.
            return new List<ConfigHistoryEntry>();
        }
    }

    public void Save(List<ConfigHistoryEntry> entries) => JsonFileStore.Save(AppPaths.ConfigHistoryFilePath, entries);

    /// <summary>Appends one entry and trims that scope's history down to <see cref="MaxEntriesPerScope"/>, oldest first dropped.</summary>
    public static void Append(List<ConfigHistoryEntry> entries, ConfigHistoryEntry entry)
    {
        entries.Add(entry);

        var forScope = entries.Where(e => e.ScopeKind == entry.ScopeKind && e.ScopeId == entry.ScopeId).OrderBy(e => e.ChangedAtUtc).ToList();
        var excess = forScope.Count - MaxEntriesPerScope;
        if (excess > 0)
        {
            var toRemove = forScope.Take(excess).Select(e => e.Id).ToHashSet();
            entries.RemoveAll(e => toRemove.Contains(e.Id));
        }
    }

    public static IEnumerable<ConfigHistoryEntry> ForScope(IEnumerable<ConfigHistoryEntry> entries, EditScopeKind scopeKind, Guid scopeId) =>
        entries.Where(e => e.ScopeKind == scopeKind && e.ScopeId == scopeId).OrderByDescending(e => e.ChangedAtUtc);
}
