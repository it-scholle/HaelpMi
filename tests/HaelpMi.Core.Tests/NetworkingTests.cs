using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Covers Teil 2, Abschnitt 6/9: Kunden-Gruppen-ID-Filterung, Boot-Call, Alarm-Wire-Format.</summary>
public class NetworkingTests
{
    private static readonly JsonSerializerOptions WireOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static LiveIdentity MakeIdentity(Guid customerGroupId, Guid deviceId, string computerName = "PC", string user = "User", string room = "Raum", string roomNumber = "1") =>
        new(customerGroupId, deviceId, computerName, user, room, roomNumber, Role.User, false, "9.9.9", 0);

    [Fact]
    public async Task AlarmSender_And_AlarmTcpListener_RoundTrip_OverLoopback()
    {
        var customerGroupId = Guid.NewGuid();
        var receiverDeviceId = Guid.NewGuid();
        var receiverIdentity = MakeIdentity(customerGroupId, receiverDeviceId, "Empfang EG", "Poststelle", "Empfangshalle", "0");
        var listener = new AlarmTcpListener(() => receiverIdentity);

        AlarmRequestMessage? received = null;
        var receivedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.AlarmReceived += (_, args) =>
        {
            received = args.Request;
            receivedSignal.TrySetResult();
        };

        var port = GetFreeTcpPort();
        listener.Start(port);

        try
        {
            var senderDeviceId = Guid.NewGuid();
            var senderIdentity = MakeIdentity(customerGroupId, senderDeviceId, "Sachbearbeitung 3", "Frau Meier", "Zimmer 214", "214");
            var sender = new AlarmSender();
            var profile = new AlarmProfile { Text = "Bitte sofort kommen!", ResponseThreshold = 1 };
            var alarmSessionId = Guid.NewGuid();
            var target = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = port };

            var result = await sender.SendAsync(profile, alarmSessionId, senderIdentity, new[] { target });

            Assert.Equal(1, result.TargetCount);
            Assert.Equal(1, result.AckedCount); // connection was established and acked (FR-13)

            await receivedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(received);
            Assert.Equal("Bitte sofort kommen!", received!.Text);
            Assert.Equal(senderDeviceId, received.SenderDeviceId);
            Assert.Equal(profile.Id, received.AlarmProfileId);
            Assert.Equal(alarmSessionId, received.AlarmSessionId);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    [Fact]
    public async Task AlarmTcpListener_DifferentCustomerGroupId_IsSilentlyIgnored()
    {
        // Teil 2, Abschnitt 6: zwei unabhängige Installationsgruppen im selben Netz
        // dürfen sich nie gegenseitig beeinflussen - eine fremde Kunden-Gruppen-ID darf
        // weder einen Alarm auslösen noch überhaupt eine Antwort provozieren.
        var receiverDeviceId = Guid.NewGuid();
        var receiverIdentity = MakeIdentity(Guid.NewGuid(), receiverDeviceId);
        var listener = new AlarmTcpListener(() => receiverIdentity);

        var received = false;
        listener.AlarmReceived += (_, _) => received = true;

        var port = GetFreeTcpPort();
        listener.Start(port);

        try
        {
            var foreignIdentity = MakeIdentity(Guid.NewGuid(), Guid.NewGuid()); // andere Kunden-Gruppen-ID
            var sender = new AlarmSender();
            var profile = new AlarmProfile { Text = "Fremdalarm" };
            var target = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = port };

            var result = await sender.SendAsync(profile, Guid.NewGuid(), foreignIdentity, new[] { target });

            // Kein Ack, weil der Listener die Nachricht verwirft, bevor er überhaupt antwortet.
            Assert.Equal(0, result.AckedCount);
            await Task.Delay(200);
            Assert.False(received);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    [Fact]
    public async Task AlarmSender_UnreachableTarget_CountsAsNotAcked_WithoutThrowing()
    {
        var sender = new AlarmSender();
        var unreachablePort = GetFreeTcpPort(); // nothing listening there
        var target = new DeviceEntry { DeviceId = Guid.NewGuid(), IpAddress = "127.0.0.1", TcpPort = unreachablePort };
        var identity = MakeIdentity(Guid.NewGuid(), Guid.NewGuid());
        var profile = new AlarmProfile { Text = "Test" };

        var result = await sender.SendAsync(profile, Guid.NewGuid(), identity, new[] { target });

        Assert.Equal(1, result.TargetCount);
        Assert.Equal(0, result.AckedCount); // 5.6: a dropped connection is "not acked", not a crash
    }

    [Fact]
    public async Task DiscoveryService_UpsertsAnnouncingDevice_AndRepliesDirectly()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Empfang EG", "Poststelle", "Empfangshalle", "0");

        DeviceEntry? updatedEntry = null;
        var updatedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.DeviceUpdated += (_, entry) =>
        {
            updatedEntry = entry;
            updatedSignal.TrySetResult();
        };
        discovery.StartListening();

        // Simulate another device's boot-call announce with a bare socket - a second real
        // DiscoveryService can't be used here because device storage is process-global
        // (one %ProgramData% per real device), so a raw socket stands in for "the other device".
        using var otherDeviceSocket = new UdpClient(0) { EnableBroadcast = true };

        var otherDeviceId = Guid.NewGuid();
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, otherDeviceId, "PC-217", "Herr Novak", "Zimmer 108", "108",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);

        var replyListenTask = otherDeviceSocket.ReceiveAsync();
        await otherDeviceSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await updatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(updatedEntry);
        Assert.Equal(otherDeviceId, updatedEntry!.DeviceId);
        Assert.Equal("PC-217", updatedEntry.ComputerName);
        Assert.Equal("Zimmer 108", updatedEntry.RoomName);
        Assert.True(updatedEntry.IsNew); // FR-24: newly discovered device starts highlighted

        // ... and the announcing device gets a direct reply back (FR-22), not silence.
        var replyResult = await replyListenTask.WaitAsync(TimeSpan.FromSeconds(5));
        var reply = JsonSerializer.Deserialize<BootCallMessage>(replyResult.Buffer, WireOptions);
        Assert.NotNull(reply);
        Assert.Equal(MessageKind.Reply, reply!.Kind);
        Assert.Equal(ownDeviceId, reply.DeviceId);
        Assert.Equal(customerGroupId, reply.CustomerGroupId);
    }

    // --- Nutzerwunsch 05.08.2026: Gossip - eine Reply bringt die komplette eigene
    // Geräteliste mit, damit ein neu startendes Gerät auch von Geräten erfährt, die im
    // Moment seines eigenen Announce gerade nicht gleichzeitig online waren. ---

    [Fact]
    public async Task DiscoveryService_ReplyToAnnounce_IncludesKnownDevices()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Empfang EG", "Poststelle", "Empfangshalle", "0");

        // Dieses Gerät kennt bereits ein drittes Gerät, das gerade offline ist - genau der
        // Fall, den reine Direkt-Antwort-Discovery nicht lösen kann.
        var alreadyKnownDeviceId = Guid.NewGuid();
        new DeviceStore().Save(new List<DeviceEntry>
        {
            new() { DeviceId = alreadyKnownDeviceId, ComputerName = "PC-OFFLINE", RoomName = "Archiv", RoomNumber = "9", User = "Frau Weber", IpAddress = "192.168.1.50", TcpPort = 51501 },
        });

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.StartListening();

        using var newDeviceSocket = new UdpClient(0) { EnableBroadcast = true };
        var newDeviceId = Guid.NewGuid();
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, newDeviceId, "PC-NEU", "Herr Neu", "Empfang", "1",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);

        var replyListenTask = newDeviceSocket.ReceiveAsync();
        await newDeviceSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        var replyResult = await replyListenTask.WaitAsync(TimeSpan.FromSeconds(5));
        var reply = JsonSerializer.Deserialize<BootCallMessage>(replyResult.Buffer, WireOptions);

        Assert.NotNull(reply);
        Assert.NotNull(reply!.KnownDevices);
        Assert.Contains(reply.KnownDevices!, d => d.DeviceId == alreadyKnownDeviceId && d.ComputerName == "PC-OFFLINE");
        Assert.DoesNotContain(reply.KnownDevices!, d => d.DeviceId == newDeviceId); // der Announcer bekommt sich nicht selbst zurückgemeldet
    }

    [Fact]
    public async Task DiscoveryService_MergesKnownDevicesFromReply_IntoOwnDeviceList()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Neu-PC", "Herr Neu", "Empfang", "1");

        var replierDeviceId = Guid.NewGuid();
        var gossipedDeviceId = Guid.NewGuid();
        var updatedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.DeviceUpdated += (_, entry) =>
        {
            if (entry.DeviceId == replierDeviceId)
            {
                updatedSignal.TrySetResult();
            }
        };
        discovery.StartListening();

        // Simuliert die Reply, die als Antwort auf unseren eigenen Announce ankäme -
        // enthält ein drittes Gerät, von dem wir nie direkt gehört haben.
        using var replierSocket = new UdpClient(0) { EnableBroadcast = true };
        var reply = new BootCallMessage(
            MessageKind.Reply, customerGroupId, replierDeviceId, "PC-Antwortend", "Frau Antwort", "Büro", "5",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow,
            new List<KnownDeviceSummary> { new(gossipedDeviceId, "PC-Weitweg", "Herr Fern", "Lager", "99", Role.User, "192.168.1.77", 51501) });
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, WireOptions);

        await replierSocket.SendAsync(replyBytes, replyBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await updatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var devices = new DeviceStore().Load();
        Assert.Contains(devices, d => d.DeviceId == replierDeviceId);
        var gossiped = Assert.Single(devices, d => d.DeviceId == gossipedDeviceId);
        Assert.Equal("PC-Weitweg", gossiped.ComputerName);
        Assert.Equal("Lager", gossiped.RoomName);
    }

    [Fact]
    public async Task DiscoveryService_DifferentCustomerGroupId_NeverUpsertsOrReplies()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var ownIdentity = MakeIdentity(Guid.NewGuid(), Guid.NewGuid());

        var updated = false;
        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.DeviceUpdated += (_, _) => updated = true;
        discovery.StartListening();

        using var otherDeviceSocket = new UdpClient(0) { EnableBroadcast = true };
        var announce = new BootCallMessage(
            MessageKind.Announce, Guid.NewGuid() /* fremde Kunden-Gruppen-ID */, Guid.NewGuid(), "PC-Fremd", "X", "Y", "1",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);

        var replyListenTask = otherDeviceSocket.ReceiveAsync();
        await otherDeviceSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        var completed = await Task.WhenAny(replyListenTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.NotSame(replyListenTask, completed); // keine Antwort - stiller Verwurf (Teil 2, Abschnitt 6)
        Assert.False(updated);
    }

    // --- Regressionsschutz 06.08.2026: dieselbe Klasse von Bug traf uns VIER Mal
    // nacheinander (ConfigSyncService 04.08., AlarmFeedbackChannel + EditLockService
    // 05.08., AlarmTcpListener 06.08.) - eine unbehandelte SocketException beim Binden
    // eines bereits belegten Ports riss jedes Mal den kompletten restlichen Agent- bzw.
    // Dashboard-Start ab, ohne jede sichtbare Fehlermeldung ("Dashboard startet nicht").
    // Jeder der vier TCP-bindenden Netzwerkdienste bekommt hier denselben Test: Port
    // vorher absichtlich belegen, Start() darf trotzdem nicht werfen, DisposeAsync danach
    // ebenfalls nicht - sonst wäre genau dieser Fehler beim nächsten Umbau wieder da.

    [Fact]
    public async Task AlarmTcpListener_Start_OnAlreadyOccupiedPort_DoesNotThrow()
    {
        var port = GetFreeTcpPort();
        using var occupier = new TcpListener(IPAddress.Loopback, port);
        occupier.Start();

        var listener = new AlarmTcpListener(() => MakeIdentity(Guid.NewGuid(), Guid.NewGuid()));

        var exception = Record.Exception(() => listener.Start(port));
        Assert.Null(exception);

        await listener.DisposeAsync(); // muss ebenfalls sauber durchlaufen, obwohl nie erfolgreich gebunden
    }

    [Fact]
    public async Task AlarmFeedbackChannel_Start_OnAlreadyOccupiedPort_DoesNotThrow()
    {
        var port = GetFreeTcpPort();
        using var occupier = new TcpListener(IPAddress.Loopback, port);
        occupier.Start();

        var channel = new AlarmFeedbackChannel(() => MakeIdentity(Guid.NewGuid(), Guid.NewGuid()));

        var exception = Record.Exception(() => channel.Start(port));
        Assert.Null(exception);

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task EditLockService_Start_OnAlreadyOccupiedPort_DoesNotThrow()
    {
        var port = GetFreeTcpPort();
        using var occupier = new TcpListener(IPAddress.Loopback, port);
        occupier.Start();

        var editLock = new EditLockService(() => MakeIdentity(Guid.NewGuid(), Guid.NewGuid()));

        var exception = Record.Exception(() => editLock.Start(port));
        Assert.Null(exception);

        await editLock.DisposeAsync();
    }

    [Fact]
    public async Task ConfigSyncService_Start_OnAlreadyOccupiedTcpPort_DoesNotThrow()
    {
        using var scope = new TestAppDataScope();
        var port = GetFreeTcpPort();
        using var occupier = new TcpListener(IPAddress.Loopback, port);
        occupier.Start();

        var configSync = new ConfigSyncService(() => MakeIdentity(Guid.NewGuid(), Guid.NewGuid()), () => new List<DeviceEntry>());

        var exception = Record.Exception(() => configSync.Start(port));
        Assert.Null(exception);

        await configSync.DisposeAsync();
    }

    [Fact]
    public void AlarmRequestMessage_RoundTrips_ThroughWireFormat()
    {
        var original = new AlarmRequestMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Sachbearbeitung 3", "Frau Meier", "Zimmer 214", "214", false,
            "Bitte kommen!", 1, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(original, WireOptions);
        var roundTripped = JsonSerializer.Deserialize<AlarmRequestMessage>(json, WireOptions);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void AlarmAckMessage_RoundTrips_ThroughWireFormat()
    {
        var original = new AlarmAckMessage(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(original, WireOptions);
        var roundTripped = JsonSerializer.Deserialize<AlarmAckMessage>(json, WireOptions);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void BootCallMessage_RoundTrips_ThroughWireFormat()
    {
        var original = new BootCallMessage(
            MessageKind.Announce, Guid.NewGuid(), Guid.NewGuid(), "PC-217", "Herr Novak", "Zimmer 108", "108",
            Role.Admin, true, 51501, "0.2.0", 3, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(original, WireOptions);
        var roundTripped = JsonSerializer.Deserialize<BootCallMessage>(json, WireOptions);

        Assert.Equal(original, roundTripped);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int GetFreeUdpPort()
    {
        using var socket = new UdpClient(0);
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
