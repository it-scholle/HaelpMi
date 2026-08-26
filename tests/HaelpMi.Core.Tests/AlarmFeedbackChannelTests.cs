using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Issue #9-Nachtrag 2: deckt die Primary/Satellite-Entscheidung von
/// <see cref="AlarmFeedbackChannel"/> ab - identisches Testprinzip wie
/// <see cref="AlarmChannelTests"/> für den Alarm-Empfangskanal (echter Loopback-TCP + ein
/// echter Named Pipe, kein Mock).
/// </summary>
public class AlarmFeedbackChannelTests
{
    private static LiveIdentity MakeIdentity(Guid customerGroupId, Guid deviceId) =>
        new(customerGroupId, deviceId, "PC", "User", "Raum", "1", Role.User, false, "9.9.9", 0);

    [Fact]
    public async Task SecondInstance_BecomesSatellite_AndReceivesOnMyWayRelayedFromPrimary()
    {
        var customerGroupId = Guid.NewGuid();
        var senderDeviceId = Guid.NewGuid();
        var identity = MakeIdentity(customerGroupId, senderDeviceId);
        var port = GetFreeTcpPort();

        await using var primary = new AlarmFeedbackChannel(() => identity);
        await using var satellite = new AlarmFeedbackChannel(() => identity);

        primary.Start(port);
        Assert.True(primary.IsPrimary);

        var satelliteReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AlarmOnMyWayMessage? received = null;
        satellite.OnMyWayReceived += (_, message) =>
        {
            received = message;
            satelliteReceived.TrySetResult();
        };

        satellite.Start(port);
        Assert.False(satellite.IsPrimary);

        var alarmSessionId = Guid.NewGuid();
        var alarmProfileId = Guid.NewGuid();
        var message = new AlarmOnMyWayMessage(
            customerGroupId, alarmProfileId, alarmSessionId, Guid.NewGuid(), "RESPONDER-PC", "Antworter",
            "Antwort-Raum", DateTimeOffset.UtcNow);
        var senderTarget = new DeviceEntry { DeviceId = senderDeviceId, IpAddress = "127.0.0.1" };

        // Wiederholt senden statt fest zu verzögern - der Satellite verbindet sich
        // asynchron über den Relay-Pipe, ein einzelner Sendeversuch könnte zu früh kommen
        // (gleiches Muster wie AlarmChannelTests).
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!satelliteReceived.Task.IsCompleted && !overallTimeout.IsCancellationRequested)
        {
            await SendOnMyWayToPortAsync(senderTarget, message, port);
            await Task.WhenAny(satelliteReceived.Task, Task.Delay(TimeSpan.FromMilliseconds(300)));
        }

        Assert.True(satelliteReceived.Task.IsCompleted, "Satellite hat die über die Primary relayte 'bin unterwegs'-Antwort nicht empfangen.");
        Assert.NotNull(received);
        Assert.Equal(alarmSessionId, received!.AlarmSessionId);
    }

    // AlarmFeedbackChannel.SendOnMyWayAsync verbindet fest gegen AppConstants.AlarmFeedbackTcpPort,
    // die Tests brauchen aber einen freien Port, um nicht mit einer echten lokalen Instanz zu
    // kollidieren - deshalb hier ein eigener minimaler Sender direkt gegen den Test-Port,
    // analog zu AlarmChannelTests' eigenem AlarmSender-Einsatz gegen einen freien Port.
    private static async Task SendOnMyWayToPortAsync(DeviceEntry target, AlarmOnMyWayMessage message, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(IPAddress.Parse(target.IpAddress), port, timeoutCts.Token);
            await using var stream = client.GetStream();

            var envelope = new AlarmFeedbackEnvelope(AlarmFeedbackMessageType.OnMyWay, NetworkSerializer.ToJsonLine(message));
            var bytes = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(envelope));
            await stream.WriteAsync(bytes, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);
        }
        catch (Exception)
        {
            // best-effort, wie das echte SendEnvelopeAsync - ein einzelner verpasster
            // Sendeversuch wird von der Wiederholungsschleife im Test aufgefangen
        }
    }

    [Fact]
    public async Task PrimaryDisposed_SatellitePromotesItselfToNewPrimary()
    {
        var identity = MakeIdentity(Guid.NewGuid(), Guid.NewGuid());
        var port = GetFreeTcpPort();

        var primary = new AlarmFeedbackChannel(() => identity);
        await using var satellite = new AlarmFeedbackChannel(() => identity);

        primary.Start(port);
        satellite.Start(port);

        // Dem Satellite Zeit geben, sich mit der Primary zu verbinden, bevor diese verschwindet.
        await Task.Delay(TimeSpan.FromSeconds(1));

        await primary.DisposeAsync();

        var promoted = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            if (satellite.IsPrimary)
            {
                promoted = true;
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        Assert.True(promoted, "Satellite hat den freigewordenen TCP-Port nach Wegfall der Primary nicht übernommen.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
