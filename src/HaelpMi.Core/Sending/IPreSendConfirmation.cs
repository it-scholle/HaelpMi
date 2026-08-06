using HaelpMi.Core.Models;

namespace HaelpMi.Core.Sending;

/// <summary>
/// PHASE 2 EXTENSION POINT (Pflichtenheft Phase 1, 5.9 / FR-7 "Sicherheitsabfrage"):
/// hook that <see cref="Networking.AlarmSender"/> calls before it actually opens any TCP
/// connection. Still unanswered - the Teil-2-Prompt doesn't address FR-7 either, so this
/// stays exactly as it was: <see cref="NoConfirmation"/> everywhere, no prompting.
/// </summary>
public interface IPreSendConfirmation
{
    Task<bool> ConfirmAsync(AlarmProfile profile, IReadOnlyList<DeviceEntry> targets, CancellationToken ct);
}

/// <summary>Default: no confirmation dialog, sends immediately.</summary>
public sealed class NoConfirmation : IPreSendConfirmation
{
    public static readonly NoConfirmation Instance = new();

    public Task<bool> ConfirmAsync(AlarmProfile profile, IReadOnlyList<DeviceEntry> targets, CancellationToken ct) =>
        Task.FromResult(true);
}
