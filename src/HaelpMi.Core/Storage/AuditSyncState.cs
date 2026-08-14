namespace HaelpMi.Core.Storage;

/// <summary>
/// Sendeseitiger Zustellstand von AuditSyncService.PushPendingAsync: pro Ziel-Admin ein
/// eigener Zähler, nicht ein einziger globaler Wasserstand. Ein einziger globaler
/// "mindestens ein Admin hat bestätigt"-Zähler würde dazu führen, dass ein zweiter, selten
/// erreichbarer Admin die früheren Einträge nie nachbekommt, sobald ein anderer Admin sie
/// schon abgeholt hat (der Sender hielte sie ja schon für "zugestellt"). Mit einem Zähler
/// pro Admin-Beziehung bekommt jeder Admin, der über genug Kontakte hinweg erreichbar ist,
/// irgendwann die komplette Historie.
/// </summary>
public sealed class AuditSyncState
{
    public Dictionary<Guid, long> LastAckedSeqByAdmin { get; init; } = new();
}

/// <summary>Lädt/speichert <see cref="AuditSyncState"/> - eine kleine Datei, keine Historie/Cap nötig.</summary>
public sealed class AuditSyncStateStore
{
    private const string FileName = "audit-sync-state.json";

    public AuditSyncState Load() =>
        JsonFileStore.Load<AuditSyncState>(Path.Combine(AppPaths.RootFolder, FileName)) ?? new AuditSyncState();

    public void Save(AuditSyncState state) =>
        JsonFileStore.Save(Path.Combine(AppPaths.RootFolder, FileName), state);
}
