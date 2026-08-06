using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Sending;

public sealed class AlarmSessionStatus
{
    public required int TargetCount { get; init; }
    public required int AckedCount { get; init; }
    public required IReadOnlyList<string> OnTheWayNames { get; init; }
    public required bool StillSending { get; init; }
}

/// <summary>
/// One triggered alarm's full lifecycle (FR-50/FR-53): repeats the send every
/// <see cref="AppConstants.AlarmRepeatInterval"/>, stops after
/// <see cref="AppConstants.AlarmMaxDuration"/>, after
/// <see cref="AppConstants.AlarmAutoStopResponseCount"/> distinct "bin unterwegs"
/// responses, or on manual <see cref="Cancel"/> - whichever comes first. Relays the
/// aggregate status to every recipient after each wave and after each new response, so
/// recipients' popups (FR-51) and this device's own hover popup (FR-53) both stay
/// current without polling.
///
/// The profile/targets passed to the constructor are a snapshot, not a live reference -
/// a Config-Sync hot-reload arriving mid-session (6., known Version-2 challenge: "darf
/// laufende Alarme nicht unterbrechen") cannot change an already-running session's
/// behavior out from under it; it only affects the *next* time this profile's hotkey fires.
/// </summary>
public sealed class RepeatingAlarmSession : IDisposable
{
    public Guid AlarmSessionId { get; } = Guid.NewGuid();
    public AlarmProfile Profile { get; }
    public IReadOnlyList<DeviceEntry> Targets { get; }

    private readonly AlarmSender _alarmSender;
    private readonly AlarmFeedbackChannel _feedbackChannel;
    private readonly LiveIdentity _ownIdentity;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly HashSet<Guid> _onTheWayResponderIds = new();
    private readonly List<string> _onTheWayNames = new();
    private readonly DateTimeOffset _startedAtUtc;
    private int _lastAckedCount;

    public event EventHandler<AlarmSessionStatus>? StatusChanged;
    public event EventHandler? Finished;

    public RepeatingAlarmSession(
        AlarmProfile profile,
        IReadOnlyList<DeviceEntry> targets,
        LiveIdentity ownIdentity,
        AlarmSender alarmSender,
        AlarmFeedbackChannel feedbackChannel,
        DateTimeOffset startedAtUtc)
    {
        Profile = profile;
        Targets = targets;
        _ownIdentity = ownIdentity;
        _alarmSender = alarmSender;
        _feedbackChannel = feedbackChannel;
        _startedAtUtc = startedAtUtc;
        _feedbackChannel.OnMyWayReceived += OnMyWayReceived;
    }

    public async Task RunAsync()
    {
        try
        {
            while (!_stopCts.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow - _startedAtUtc >= AppConstants.AlarmMaxDuration)
                {
                    break;
                }

                var result = await _alarmSender.SendAsync(Profile, AlarmSessionId, _ownIdentity, Targets, ct: _stopCts.Token);
                _lastAckedCount = result.AckedCount;
                await RaiseAndRelayAsync(stillSending: true);

                try
                {
                    await Task.Delay(AppConstants.AlarmRepeatInterval, _stopCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // cancelled mid-send - falls through to the final "stopped" relay below
        }

        await RaiseAndRelayAsync(stillSending: false);
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Manual "Abbrechen" (FR-50).</summary>
    public void Cancel() => _stopCts.Cancel();

    private void OnMyWayReceived(object? sender, AlarmOnMyWayMessage message)
    {
        if (message.AlarmProfileId != Profile.Id || message.AlarmSessionId != AlarmSessionId)
        {
            return;
        }

        if (!_onTheWayResponderIds.Add(message.ResponderDeviceId))
        {
            return; // already counted this device for this session
        }

        _onTheWayNames.Add($"{message.ResponderRoomName} - {message.ResponderComputerName} ({message.ResponderUser})");

        // Fire-and-forget: RelayStatusAsync/SendEnvelopeAsync already swallow per-target
        // failures internally, so there is nothing here that can throw unobserved.
        _ = RaiseAndRelayAsync(stillSending: true);

        if (_onTheWayResponderIds.Count >= AppConstants.AlarmAutoStopResponseCount)
        {
            _stopCts.Cancel(); // FR-50: auto-stop once enough people are on their way
        }
    }

    private async Task RaiseAndRelayAsync(bool stillSending)
    {
        var status = new AlarmSessionStatus
        {
            TargetCount = Targets.Count,
            AckedCount = _lastAckedCount,
            OnTheWayNames = _onTheWayNames.ToList(),
            StillSending = stillSending,
        };
        StatusChanged?.Invoke(this, status);

        var relay = new AlarmStatusRelayMessage(
            _ownIdentity.CustomerGroupId, Profile.Id, AlarmSessionId,
            Targets.Count, _lastAckedCount, status.OnTheWayNames, stillSending, DateTimeOffset.UtcNow);
        await _feedbackChannel.RelayStatusAsync(Targets, relay);
    }

    public void Dispose()
    {
        _feedbackChannel.OnMyWayReceived -= OnMyWayReceived;
        _stopCts.Dispose();
    }
}
