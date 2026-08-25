using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Issue #9 (Fast User Switching ohne Logout/Reboot): deckt die Primary/Satellite-
/// Entscheidung von <see cref="AlarmChannel"/> über echten Loopback-TCP + einen echten
/// Named Pipe ab (kein Mock - beide sind reine lokale IPC, verändern das ausführende
/// System nicht, gleiches Prinzip wie die bestehenden AlarmTcpListener-Tests in
/// NetworkingTests.cs).
/// </summary>
public class AlarmChannelTests
{
    private static LiveIdentity MakeIdentity(Guid customerGroupId, Guid deviceId) =>
        new(customerGroupId, deviceId, "PC", "User", "Raum", "1", Role.User, false, "9.9.9", 0);

    [Fact]
    public async Task SecondInstance_BecomesSatellite_AndReceivesAlarmRelayedFromPrimary()
    {
        var customerGroupId = Guid.NewGuid();
        var receiverDeviceId = Guid.NewGuid();
        var identity = MakeIdentity(customerGroupId, receiverDeviceId);
        var port = GetFreeTcpPort();

        await using var primary = new AlarmChannel(() => identity);
        await using var satellite = new AlarmChannel(() => identity);

        primary.Start(port);
        Assert.True(primary.IsPrimary);

        var satelliteReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        satellite.AlarmReceived += (_, _) => satelliteReceived.TrySetResult();

        satellite.Start(port);
        Assert.False(satellite.IsPrimary);

        // Alarm mehrfach senden statt fest zu verzögern - der Satellite verbindet sich
        // asynchron über den Relay-Pipe, ein einzelner Sendeversuch könnte zu früh kommen.
        var senderIdentity = MakeIdentity(customerGroupId, Guid.NewGuid());
        var target = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = port };
        var profile = new AlarmProfile { Text = "Testalarm", ResponseThreshold = 1 };
        var sender = new AlarmSender();

        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!satelliteReceived.Task.IsCompleted && !overallTimeout.IsCancellationRequested)
        {
            await sender.SendAsync(profile, Guid.NewGuid(), senderIdentity, new[] { target });
            await Task.WhenAny(satelliteReceived.Task, Task.Delay(TimeSpan.FromMilliseconds(300)));
        }

        Assert.True(satelliteReceived.Task.IsCompleted, "Satellite hat den über die Primary relayten Alarm nicht empfangen.");
    }

    [Fact]
    public async Task PrimaryDisposed_SatellitePromotesItselfToNewPrimary()
    {
        var identity = MakeIdentity(Guid.NewGuid(), Guid.NewGuid());
        var port = GetFreeTcpPort();

        var primary = new AlarmChannel(() => identity);
        await using var satellite = new AlarmChannel(() => identity);

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
