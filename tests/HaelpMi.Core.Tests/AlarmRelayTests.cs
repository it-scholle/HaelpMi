using System.Net;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Deckt <see cref="AlarmRelayServer"/>/<see cref="AlarmRelayClient"/> ab (Fast-User-Switching-
/// Fix 17.08.2026, s. Klassendoku dort): die lokale Weiterleitung eines bereits empfangenen
/// Alarms an eine "Satellite"-Sitzung sowie die Erkennung eines Verbindungsabbruchs, die die
/// Übernahme des exklusiven Alarm-Ports in HaelpMi.Agent auslöst.
/// </summary>
public class AlarmRelayTests
{
    private static AlarmRequestMessage MakeRequest() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "PC-217", "Herr Empfang", "Zimmer 108", "108", false,
        "Bitte sofort kommen!", 1, DateTimeOffset.UtcNow);

    [Fact]
    public async Task BroadcastAsync_ConnectedClient_ReceivesTheSameAlarm()
    {
        await using var server = new AlarmRelayServer();
        server.Start();

        await using var client = new AlarmRelayClient();
        AlarmReceivedEventArgs? received = null;
        var receivedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AlarmRelayed += (_, args) =>
        {
            received = args;
            receivedSignal.TrySetResult();
        };
        client.Start();

        // Kurze Wartezeit, bis der Client tatsächlich verbunden ist, bevor gesendet wird -
        // analog zu den Discovery-Tests, die ebenfalls erst StartListening/Connect abwarten.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var request = MakeRequest();
        await server.BroadcastAsync(request, "192.168.1.42");

        await receivedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(received);
        Assert.Equal(request.AlarmSessionId, received!.Request.AlarmSessionId);
        Assert.Equal(request.Text, received.Request.Text);
        Assert.Equal(IPAddress.Parse("192.168.1.42"), received.SenderAddress);
    }

    [Fact]
    public async Task ConnectionLost_FiresWhenServerDisappears()
    {
        // Primary-Sitzung meldet sich ab (Server wird disposed, s. AlarmRelayServer-Klassendoku)
        // - die Satellite-Instanz muss das bemerken, um sich anschließend selbst um den Port zu
        // bewerben (s. HaelpMi.Agent App.xaml.cs TryBecomePrimaryOrSatellite).
        var server = new AlarmRelayServer();
        server.Start();

        var client = new AlarmRelayClient();
        var connectionLostSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionLost += (_, _) => connectionLostSignal.TrySetResult();
        client.Start();

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await server.DisposeAsync();

        await connectionLostSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await client.DisposeAsync();
    }

    [Fact]
    public async Task ConnectionLost_FiresWhenNoServerIsRunningAtAll()
    {
        // Startreihenfolge-Race (zwei Sitzungen starten fast gleichzeitig, die Primary hat ihre
        // Relay-Pipe noch nicht aufgemacht): ConnectAsync schlägt fehl, muss aber genauso als
        // ConnectionLost gemeldet werden wie ein späterer Abbruch, nicht stillschweigend
        // verschluckt werden - sonst würde diese Sitzung nie erneut versuchen, Primary oder
        // Satellite zu werden.
        await using var client = new AlarmRelayClient();
        var connectionLostSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionLost += (_, _) => connectionLostSignal.TrySetResult();
        client.Start();

        await connectionLostSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
