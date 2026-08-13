using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Sending;

namespace HaelpMi.Core.Networking;

public sealed class AlarmSendProgress
{
    public required DeviceEntry Target { get; init; }
    public required bool Acked { get; init; }
}

public sealed class AlarmSendResult
{
    public required int TargetCount { get; init; }
    public required int AckedCount { get; init; }
}

/// <summary>
/// TCP client side of the alarm channel (5.1/5.2, FR-5): opens one direct connection
/// per target in parallel, no broker, no relay through other devices. Each connection
/// writes one <see cref="AlarmRequestMessage"/> and awaits one
/// <see cref="AlarmAckMessage"/> before closing, which is also how the "Empfangen: x
/// von y" count (FR-14) is derived - x is simply how many of those connections got an
/// ack back before <see cref="AppConstants.AlarmAckTimeout"/>.
///
/// This is the single-shot building block: one call = one wave of connections. The
/// repeat-every-5-seconds / auto-stop orchestration (Teil 2, Abschnitt 7) is a thin
/// wrapper around repeated calls to this with the *same* <c>alarmSessionId</c> each
/// time - see <c>RepeatingAlarmSender</c> in HaelpMi.Agent.
/// </summary>
public sealed class AlarmSender
{
    private readonly Action<string>? _audit;

    public AlarmSender(Action<string>? audit = null)
    {
        _audit = audit;
    }

    public async Task<AlarmSendResult> SendAsync(
        AlarmProfile profile,
        Guid alarmSessionId,
        LiveIdentity ownIdentity,
        IReadOnlyList<DeviceEntry> targets,
        bool isTest = false,
        IPreSendConfirmation? confirmation = null,
        IProgress<AlarmSendProgress>? progress = null,
        CancellationToken ct = default)
    {
        confirmation ??= NoConfirmation.Instance;
        var proceed = await confirmation.ConfirmAsync(profile, targets, ct);
        if (!proceed)
        {
            return new AlarmSendResult { TargetCount = targets.Count, AckedCount = 0 };
        }

        var request = new AlarmRequestMessage(
            ownIdentity.CustomerGroupId, profile.Id, alarmSessionId, ownIdentity.DeviceId,
            ownIdentity.ComputerName, ownIdentity.User, ownIdentity.RoomName, ownIdentity.RoomNumber,
            ownIdentity.IsRemoteSession, profile.Text, profile.ResponseThreshold, DateTimeOffset.UtcNow, IsTest: isTest);
        _audit?.Invoke($"alarm sent alarmProfileId={profile.Id} sessionId={alarmSessionId} targetCount={targets.Count} isTest={isTest}");

        var ackedCount = 0;
        var sendTasks = targets.Select(async target =>
        {
            var acked = await SendToOneAsync(request, target, ct);
            if (acked)
            {
                Interlocked.Increment(ref ackedCount);
            }
            progress?.Report(new AlarmSendProgress { Target = target, Acked = acked });
        });

        await Task.WhenAll(sendTasks);

        return new AlarmSendResult { TargetCount = targets.Count, AckedCount = ackedCount };
    }

    private static async Task<bool> SendToOneAsync(AlarmRequestMessage request, DeviceEntry target, CancellationToken ct)
    {
        try
        {
            if (!IPAddress.TryParse(target.IpAddress, out var address))
            {
                return false;
            }

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(AppConstants.AlarmAckTimeout);

            await client.ConnectAsync(address, target.TcpPort, timeoutCts.Token);
            await using var stream = client.GetStream();

            var payload = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(request));
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            using var reader = new StreamReader(stream, NetworkSerializer.Encoding);
            var line = await reader.ReadLineAsync(timeoutCts.Token);
            if (line is null)
            {
                return false;
            }

            var ack = NetworkSerializer.FromJsonLine<AlarmAckMessage>(line);
            return ack is not null && ack.AlarmProfileId == request.AlarmProfileId && ack.AlarmSessionId == request.AlarmSessionId;
        }
        catch (Exception)
        {
            // Unreachable/offline target, refused connection, timeout, etc. - counts as
            // "not (yet) acked" rather than failing the whole send (5.6 known challenge).
            return false;
        }
    }
}
