namespace HaelpMi.Core.Storage;

/// <summary>Persistierter Kill-Switch-Zustand für die Update-Pipeline (Abschnitt 11).</summary>
public sealed class UpdateLockoutState
{
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
}

/// <summary>
/// "Kill-Switch bei wiederholten Fehlschlägen": statt endlos jeden neu beworbenen Peer-
/// Versionswechsel erneut zu versuchen, merkt sich das Gerät fehlgeschlagene Versuche
/// lokal und pausiert für <see cref="Models.AppConstants.UpdateLockoutDuration"/>, sobald
/// <see cref="Models.AppConstants.UpdateMaxConsecutiveFailures"/> erreicht ist.
/// </summary>
public sealed class UpdateLockoutStore
{
    private static string FilePath => Path.Combine(AppPaths.RootFolder, "update-lockout.json");

    public UpdateLockoutState Load() => JsonFileStore.Load<UpdateLockoutState>(FilePath) ?? new UpdateLockoutState();

    public int RecordFailure()
    {
        var state = Load();
        state.ConsecutiveFailures++;
        JsonFileStore.Save(FilePath, state);
        return state.ConsecutiveFailures;
    }

    public void ResetFailures()
    {
        var state = Load();
        if (state.ConsecutiveFailures == 0 && state.LockedUntilUtc is null)
        {
            return;
        }

        state.ConsecutiveFailures = 0;
        state.LockedUntilUtc = null;
        JsonFileStore.Save(FilePath, state);
    }

    public void SetLockout(DateTimeOffset untilUtc)
    {
        var state = Load();
        state.LockedUntilUtc = untilUtc;
        JsonFileStore.Save(FilePath, state);
    }
}
