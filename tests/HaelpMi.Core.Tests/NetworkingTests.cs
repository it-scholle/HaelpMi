using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Runtime;
using HaelpMi.Core.Security;
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
            new List<KnownDeviceSummary> { new(gossipedDeviceId, "PC-Weitweg", "Herr Fern", "Lager", "99", Role.User, "192.168.1.77", 51501, DateTimeOffset.UtcNow) });
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, WireOptions);

        await replierSocket.SendAsync(replyBytes, replyBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await updatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var devices = new DeviceStore().Load();
        Assert.Contains(devices, d => d.DeviceId == replierDeviceId);
        var gossiped = Assert.Single(devices, d => d.DeviceId == gossipedDeviceId);
        Assert.Equal("PC-Weitweg", gossiped.ComputerName);
        Assert.Equal("Lager", gossiped.RoomName);
    }

    // --- Wellen-Rollout (Nutzerwunsch 16.08.2026): UpdateOrchestrator.IsMyTurn schätzt die
    // erlaubte Wellenbreite aus DeviceEntry.LastKnownProgramVersion - die beiden folgenden
    // Tests prüfen, dass dieses Feld tatsächlich sowohl aus dem direkten Boot-Call-Kontakt
    // als auch aus dem Gossip-Anhang einer Reply ankommt. ---

    [Fact]
    public async Task DiscoveryService_DirectBootCall_PropagatesProgramVersionIntoDeviceEntry()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Neu-PC", "Herr Neu", "Empfang", "1");

        var peerDeviceId = Guid.NewGuid();
        var updatedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.DeviceUpdated += (_, entry) =>
        {
            if (entry.DeviceId == peerDeviceId)
            {
                updatedSignal.TrySetResult();
            }
        };
        discovery.StartListening();

        using var peerSocket = new UdpClient(0) { EnableBroadcast = true };
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, peerDeviceId, "PC-Peer", "Frau Peer", "Büro", "5",
            Role.User, false, 51999, "0.31.0", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);

        await peerSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await updatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var devices = new DeviceStore().Load();
        var peer = Assert.Single(devices, d => d.DeviceId == peerDeviceId);
        Assert.Equal("0.31.0", peer.LastKnownProgramVersion);
    }

    [Fact]
    public async Task DiscoveryService_MergesKnownDevicesFromReply_AlsoPropagatesGossipedProgramVersion()
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

        using var replierSocket = new UdpClient(0) { EnableBroadcast = true };
        var reply = new BootCallMessage(
            MessageKind.Reply, customerGroupId, replierDeviceId, "PC-Antwortend", "Frau Antwort", "Büro", "5",
            Role.User, false, 51999, "0.29.1", 0, DateTimeOffset.UtcNow,
            new List<KnownDeviceSummary> { new(gossipedDeviceId, "PC-Weitweg", "Herr Fern", "Lager", "99", Role.User, "192.168.1.77", 51501, DateTimeOffset.UtcNow, "0.31.0") });
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, WireOptions);

        await replierSocket.SendAsync(replyBytes, replyBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await updatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var devices = new DeviceStore().Load();
        var gossiped = Assert.Single(devices, d => d.DeviceId == gossipedDeviceId);
        Assert.Equal("0.31.0", gossiped.LastKnownProgramVersion);
    }

    // --- Multi-VLAN-Bridge-Seed (Nutzerwunsch 13.08.2026): "Admin als so eine Art erster
    // Peer" für geroutete, aber nicht per Broadcast erreichbare Subnetze/VLANs hinweg. Alle
    // drei Tests zielen auf 127.0.0.x-Adressen statt 127.0.0.1: explizit auf eine konkrete
    // Loopback-Adresse gebundene Sockets empfangen deterministisch nur, was genau dorthin
    // adressiert ist - der DiscoveryService selbst bindet parallel IPAddress.Any auf
    // demselben Port, ein Test über 127.0.0.1 könnte sonst nicht zuverlässig unterscheiden,
    // ob ein empfangenes Paket vom eigenen Socket oder vom simulierten Bridge-Seed-Ziel kam. ---

    [Fact]
    public async Task DiscoveryService_AnnounceAsync_AlsoUnicastsToConfiguredBridgeSeedAddress()
    {
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId);

        const string seedAddress = "127.0.0.2";
        using var seedSocket = new UdpClient(new IPEndPoint(IPAddress.Parse(seedAddress), discoveryPort));

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort, bridgeSeedAddressProvider: () => new[] { seedAddress });
        discovery.StartListening();

        var seedReceiveTask = seedSocket.ReceiveAsync();
        await discovery.AnnounceAsync();

        var result = await seedReceiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var received = JsonSerializer.Deserialize<BootCallMessage>(result.Buffer, WireOptions);
        Assert.NotNull(received);
        Assert.Equal(MessageKind.Announce, received!.Kind);
        Assert.Equal(ownDeviceId, received.DeviceId);
        Assert.Equal(customerGroupId, received.CustomerGroupId);
    }

    [Fact]
    public async Task DiscoveryService_AnnounceAsync_UnicastsToAllConfiguredBridgeSeedAddresses()
    {
        // Nutzerwunsch 15.08.2026: mehrere Bridge-Geräte statt einem - jede konfigurierte
        // Adresse muss den Announce erhalten, nicht nur die erste.
        var discoveryPort = GetFreeUdpPort();
        var ownIdentity = MakeIdentity(Guid.NewGuid(), Guid.NewGuid());

        const string firstSeedAddress = "127.0.0.3";
        const string secondSeedAddress = "127.0.0.4";
        using var firstSeedSocket = new UdpClient(new IPEndPoint(IPAddress.Parse(firstSeedAddress), discoveryPort));
        using var secondSeedSocket = new UdpClient(new IPEndPoint(IPAddress.Parse(secondSeedAddress), discoveryPort));

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort,
            bridgeSeedAddressProvider: () => new[] { firstSeedAddress, secondSeedAddress });
        discovery.StartListening();

        var firstReceiveTask = firstSeedSocket.ReceiveAsync();
        var secondReceiveTask = secondSeedSocket.ReceiveAsync();
        await discovery.AnnounceAsync();

        await firstReceiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        await secondReceiveTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DiscoveryService_AnnounceAsync_BadSeedAddressDoesNotBlockOtherSeedAddresses()
    {
        // Ein einzelner kaputter/nicht auflösbarer Eintrag in der Liste darf weder die
        // übrigen konfigurierten Bridge-Seeds noch AnnounceAsync selbst blockieren.
        var discoveryPort = GetFreeUdpPort();
        var ownIdentity = MakeIdentity(Guid.NewGuid(), Guid.NewGuid());

        const string workingSeedAddress = "127.0.0.5";
        using var workingSeedSocket = new UdpClient(new IPEndPoint(IPAddress.Parse(workingSeedAddress), discoveryPort));

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort,
            bridgeSeedAddressProvider: () => new[] { "not-a-real-host.invalid", workingSeedAddress });
        discovery.StartListening();

        var workingReceiveTask = workingSeedSocket.ReceiveAsync();
        var exception = await Record.ExceptionAsync(() => discovery.AnnounceAsync());
        Assert.Null(exception);

        await workingReceiveTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DiscoveryService_AnnounceAsync_WithoutConfiguredBridgeSeedAddress_DoesNotThrow()
    {
        // Normalfall (kein Multi-VLAN-Bootstrap konfiguriert, kein bridgeSeedAddressProvider
        // übergeben) - AnnounceAsync darf dadurch nicht fehlschlagen, siehe alle anderen
        // Tests dieser Datei, die den Parameter ebenfalls weglassen.
        var discoveryPort = GetFreeUdpPort();
        var ownIdentity = MakeIdentity(Guid.NewGuid(), Guid.NewGuid());

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.StartListening();

        var exception = await Record.ExceptionAsync(() => discovery.AnnounceAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task DiscoveryService_LearningNewGossipedDevice_TriggersImmediateReAnnounce()
    {
        // Regressionsschutz 13.08.2026, gleiche Fehlerklasse wie PeerConfigVersionObserved
        // (11.08.2026): ohne diesen sofortigen Re-Announce würden bereits laufende lokale
        // Peers von einem neu über die Brücke gelernten Fremdsubnetz-Gerät erst bei ihrem
        // eigenen nächsten Boot erfahren, statt sofort.
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Neu-PC", "Herr Neu", "Empfang", "1");

        const string seedAddress = "127.0.0.3";
        using var seedSocket = new UdpClient(new IPEndPoint(IPAddress.Parse(seedAddress), discoveryPort));
        var seedReceiveTask = seedSocket.ReceiveAsync();

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort, bridgeSeedAddressProvider: () => new[] { seedAddress });
        discovery.StartListening();

        using var replierSocket = new UdpClient(0) { EnableBroadcast = true };
        var replierDeviceId = Guid.NewGuid();
        var gossipedDeviceId = Guid.NewGuid(); // bislang komplett unbekannt
        var reply = new BootCallMessage(
            MessageKind.Reply, customerGroupId, replierDeviceId, "PC-Antwortend", "Frau Antwort", "Büro", "5",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow,
            new List<KnownDeviceSummary> { new(gossipedDeviceId, "PC-Weitweg", "Herr Fern", "Lager", "99", Role.User, "192.168.1.77", 51501, DateTimeOffset.UtcNow) });
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, WireOptions);
        await replierSocket.SendAsync(replyBytes, replyBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        // Nachweis über den Bridge-Seed-Unicast statt über den realen Broadcast selbst -
        // deterministisch, ohne auf Broadcast-über-Loopback-Zustellung angewiesen zu sein.
        var seedResult = await seedReceiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var seedReceived = JsonSerializer.Deserialize<BootCallMessage>(seedResult.Buffer, WireOptions);
        Assert.NotNull(seedReceived);
        Assert.Equal(MessageKind.Announce, seedReceived!.Kind);
        Assert.Equal(ownDeviceId, seedReceived.DeviceId);
    }

    [Fact]
    public async Task DiscoveryService_RefreshingAlreadyKnownDevice_DoesNotTriggerReAnnounce()
    {
        // Gegenprobe zum Test oben: ein reiner Refresh (z. B. "Erneut suchen" trifft auf ein
        // bereits bekanntes Gerät) darf keinen weiteren Re-Announce auslösen - sonst würde
        // jede normale Discovery-Aktivität unnötigen zusätzlichen Netzverkehr erzeugen.
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, Guid.NewGuid());

        const string seedAddress = "127.0.0.4";
        using var seedSocket = new UdpClient(new IPEndPoint(IPAddress.Parse(seedAddress), discoveryPort));

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort, bridgeSeedAddressProvider: () => new[] { seedAddress });
        discovery.StartListening();

        using var peerSocket = new UdpClient(0) { EnableBroadcast = true };
        var peerDeviceId = Guid.NewGuid();
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, peerDeviceId, "PC-Bekannt", "Herr Bekannt", "Büro", "3",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);

        // Erstkontakt: das Gerät ist neu, löst also legitim selbst einen Re-Announce an den
        // Bridge-Seed aus (siehe Test oben) - hier bewusst abwarten/konsumieren, damit er
        // unten nicht mit dem eigentlich zu prüfenden zweiten Durchlauf verwechselt wird.
        var firstReplyTask = peerSocket.ReceiveAsync();
        var firstSeedReceiveTask = seedSocket.ReceiveAsync();
        await peerSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));
        await firstReplyTask.WaitAsync(TimeSpan.FromSeconds(5));
        await firstSeedReceiveTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Zweiter Announce DESSELBEN, jetzt schon bekannten Geräts - reiner Refresh, kein
        // neues Gerät, darf keinen weiteren Re-Announce auslösen.
        var secondReplyTask = peerSocket.ReceiveAsync();
        var secondSeedReceiveTask = seedSocket.ReceiveAsync();
        await peerSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));
        await secondReplyTask.WaitAsync(TimeSpan.FromSeconds(5));

        var completed = await Task.WhenAny(secondSeedReceiveTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.NotSame(secondSeedReceiveTask, completed);
    }

    // --- Regressionsschutz 11.08.2026: drei frisch installierte Geräte blieben ohne
    // Config, obwohl der Admin sie längst über S/E "alle" eingerichtet hatte - erst ein
    // erneuter Config-Sync-Broadcast (Empfänger entfernt/wieder hinzugefügt) hat sie
    // erreicht. Ursache: der Boot-Call tauscht ConfigVersion zwar aus, aber nichts wertete
    // sie aus, solange die ProgramVersion gleich war. PeerConfigVersionObserved schließt
    // diese Lücke, symmetrisch für beide Seiten des Austauschs. ---

    [Fact]
    public async Task DiscoveryService_PeerWithNewerConfigVersion_RaisesPeerConfigVersionObserved()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        // Eigene ConfigVersion 0 - genau der Zustand eines frisch installierten Geräts,
        // das noch nie einen Config-Sync-Broadcast erhalten hat.
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId);

        PeerConfigVersionInfo? observed = null;
        var observedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.PeerConfigVersionObserved += (_, info) =>
        {
            observed = info;
            observedSignal.TrySetResult();
        };
        discovery.StartListening();

        using var peerSocket = new UdpClient(0) { EnableBroadcast = true };
        var peerDeviceId = Guid.NewGuid();
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, peerDeviceId, "PC-ADMIN", "Admin", "Leitstelle", "0",
            Role.Admin, false, 51999, "9.9.9", 5 /* schon eingerichtet */, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);
        await peerSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await observedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(observed);
        Assert.Equal(peerDeviceId, observed!.DeviceId);
        Assert.Equal(5, observed.ConfigVersion);
    }

    [Fact]
    public async Task ConfigSyncService_PullsAndAppliesNewerConfig_WhenPeerConfigVersionObserved()
    {
        using var scope = new TestAppDataScope();
        var customerGroupId = Guid.NewGuid();
        var ownDeviceId = Guid.NewGuid();

        // Frisch installiert: settings.json existiert (vom Installer), aber noch nie ein
        // Config-Sync angewendet.
        new SettingsStore().Save(new OwnSettings
        {
            DeviceId = ownDeviceId,
            CustomerGroupId = customerGroupId,
            AppliedConfigVersion = 0,
        });
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId);

        // Rohsocket statt eines zweiten echten ConfigSyncService: ein zweiter Dienst würde
        // denselben AppPaths-Test-Root (und damit dieselbe settings.json) teilen - gleiches
        // Muster wie bei den DiscoveryService-Tests oben ("Gerätespeicherung ist
        // prozessweit").
        var peerDeviceId = Guid.NewGuid();
        // ConfigSyncService.PullFromAsync verbindet fest gegen AppConstants.ConfigSyncTcpPort
        // (kein pro-Gerät-Port wie DeviceEntry.TcpPort - der ist der Alarm-Port) - der
        // simulierte Peer muss also genau dort lauschen, kein frei gewählter Port.
        using var peerListener = new TcpListener(IPAddress.Loopback, AppConstants.ConfigSyncTcpPort);
        peerListener.Start();
        var peerTask = Task.Run(async () =>
        {
            using var client = await peerListener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var line = await BoundedLineReader.ReadLineAsync(stream, CancellationToken.None);
            var request = line is null ? null : JsonSerializer.Deserialize<ConfigSyncPullRequestMessage>(line, WireOptions);
            Assert.NotNull(request);
            Assert.Equal(ownDeviceId, request!.RequesterDeviceId);

            var response = new ConfigSyncPullResponseMessage(customerGroupId, new SharedConfig { ConfigVersion = 5 });
            var responseBytes = NetworkSerializer.Encoding.GetBytes(JsonSerializer.Serialize(response, WireOptions) + "\n");
            await stream.WriteAsync(responseBytes);
        });

        var deviceList = new List<DeviceEntry> { new() { DeviceId = peerDeviceId, IpAddress = "127.0.0.1" } };

        var appliedSignal = new TaskCompletionSource<SharedConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var configSync = new ConfigSyncService(() => ownIdentity, () => deviceList);
        configSync.ConfigApplied += (_, config) => appliedSignal.TrySetResult(config);

        // Simuliert exakt das, was DiscoveryService.PeerConfigVersionObserved beim
        // Boot-Call auslösen würde.
        configSync.OnPeerConfigVersionObserved(null, new PeerConfigVersionInfo { DeviceId = peerDeviceId, ConfigVersion = 5 });

        var applied = await appliedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(5, applied.ConfigVersion);

        Assert.Equal(5, new SettingsStore().Load().AppliedConfigVersion);
        await peerTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // Regressionsschutz 17.08.2026 (Fehlerbericht "neu beigetretenes Gerät bekommt keine
    // Config, bis der Admin neu startet"): der eigentliche Bug saß in HaelpMi.Agent/
    // App.xaml.cs' BuildIdentity(), das ConfigVersion aus einem einmalig bei OnStartup
    // gecachten OwnSettings-Feld las statt bei jedem Aufruf frisch von der Platte -
    // ConfigSyncService.ApplyToSelf schreibt eine per Hot-Reload übernommene ConfigVersion
    // über eine EIGENE, separate SettingsStore-Instanz weg (siehe Klassenkommentar dort),
    // der gecachte Snapshot im Agent bekam davon nie etwas mit. Der Test unten spielt genau
    // diesen zeitlichen Ablauf mit zwei SettingsStore-Instanzen nach (wie App.xaml.cs vs.
    // ConfigSyncService in Produktion) und dokumentiert per Kontrast beide Verhalten: ein
    // einmal gecachter identityProvider (Bug) meldet in seinem Boot-Call-Reply weiterhin die
    // alte Version, ein bei jedem Aufruf frisch ladender (Fix, wie BuildIdentity() jetzt)
    // meldet die neue.
    [Fact]
    public async Task DiscoveryService_Reply_WithCachedIdentitySnapshot_StillReportsStaleConfigVersion()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var ownDeviceId = Guid.NewGuid();
        var deployment = new DeploymentInfo { CustomerGroupId = customerGroupId, Role = Role.User };

        var settingsStore = new SettingsStore();
        settingsStore.Save(new OwnSettings { DeviceId = ownDeviceId, CustomerGroupId = customerGroupId, AppliedConfigVersion = 0 });

        // Bug-Nachstellung: identityProvider ist ein Closure über einen EINMAL geladenen
        // OwnSettings-Snapshot - genau das alte BuildIdentity()-Verhalten.
        var cachedSnapshot = settingsStore.Load();
        Func<LiveIdentity> staleIdentityProvider = () => LiveIdentityFactory.Create(cachedSnapshot, deployment);

        await using var discovery = new DiscoveryService(staleIdentityProvider, discoveryPort: discoveryPort);
        discovery.StartListening();

        // Simuliert ConfigSyncService.ApplyToSelf: eine ANDERE SettingsStore-Instanz schreibt
        // inzwischen die per Hot-Reload übernommene neue Version auf dieselbe Platte.
        new SettingsStore().Save(new OwnSettings { DeviceId = ownDeviceId, CustomerGroupId = customerGroupId, AppliedConfigVersion = 5 });

        var reply = await SendAnnounceAndReceiveReplyAsync(discoveryPort, customerGroupId);

        // Der Bug: die Reply meldet weiterhin 0, obwohl auf der Platte längst 5 steht - ein
        // neu beigetretenes Gerät (ebenfalls bei 0) hätte PeerConfigVersionObserved nie
        // ausgelöst (siehe DiscoveryService.cs:404, message.ConfigVersion > ownIdentity.ConfigVersion).
        Assert.Equal(0, reply.ConfigVersion);
    }

    [Fact]
    public async Task DiscoveryService_Reply_WithFreshlyLoadedIdentity_ReportsCurrentConfigVersion()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var ownDeviceId = Guid.NewGuid();
        var deployment = new DeploymentInfo { CustomerGroupId = customerGroupId, Role = Role.User };

        var settingsStore = new SettingsStore();
        settingsStore.Save(new OwnSettings { DeviceId = ownDeviceId, CustomerGroupId = customerGroupId, AppliedConfigVersion = 0 });

        // Fix-Muster: identityProvider lädt bei JEDEM Aufruf frisch (wie BuildIdentity() jetzt).
        Func<LiveIdentity> freshIdentityProvider = () => LiveIdentityFactory.Create(settingsStore.Load(), deployment);

        await using var discovery = new DiscoveryService(freshIdentityProvider, discoveryPort: discoveryPort);
        discovery.StartListening();

        // Simuliert ConfigSyncService.ApplyToSelf über eine separate SettingsStore-Instanz,
        // exakt wie im "stale"-Gegenstück oben.
        new SettingsStore().Save(new OwnSettings { DeviceId = ownDeviceId, CustomerGroupId = customerGroupId, AppliedConfigVersion = 5 });

        var reply = await SendAnnounceAndReceiveReplyAsync(discoveryPort, customerGroupId);

        Assert.Equal(5, reply.ConfigVersion);
    }

    private static async Task<BootCallMessage> SendAnnounceAndReceiveReplyAsync(int discoveryPort, Guid customerGroupId)
    {
        using var peerSocket = new UdpClient(0) { EnableBroadcast = true };
        var peerDeviceId = Guid.NewGuid();
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, peerDeviceId, "PC-PERSONAL", "Personal", "Zimmer 3", "3",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);
        await peerSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        var replyResult = await peerSocket.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var reply = JsonSerializer.Deserialize<BootCallMessage>(replyResult.Buffer, WireOptions);
        Assert.NotNull(reply);
        return reply!;
    }

    [Fact]
    public async Task ConfigSyncService_PullsAndAppliesNewerConfig_ThroughSecureEnvelope_WhenPeerIsCapable()
    {
        // LAN-Verschlüsselung (CLAUDE.md "Lizenz & Secrets"): gleicher Testaufbau wie
        // ConfigSyncService_PullsAndAppliesNewerConfig_WhenPeerConfigVersionObserved, aber
        // mit Gruppenschlüssel + als verschlüsselungsfähig+gepinnt markiertem Peer - der
        // simulierte Rohsocket-Peer verschlüsselt/entschlüsselt manuell über
        // SecureEnvelopeCodec, mit demselben (prozessweit gecachten) DeviceIdentityStore-
        // Schlüssel wie ConfigSyncService selbst (siehe AuditSyncService-Pendant-Test für
        // dieselbe bewusste Vereinfachung).
        using var scope = new TestAppDataScope();
        DeviceIdentityStore.ResetCacheForTests();
        var customerGroupId = Guid.NewGuid();
        var ownDeviceId = Guid.NewGuid();
        var groupKeyBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var sharedKeyPair = DeviceIdentityStore.LoadOrCreate();

        new SettingsStore().Save(new OwnSettings
        {
            DeviceId = ownDeviceId,
            CustomerGroupId = customerGroupId,
            AppliedConfigVersion = 0,
        });
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId);

        var peerDeviceId = Guid.NewGuid();
        using var peerListener = new TcpListener(IPAddress.Loopback, AppConstants.ConfigSyncTcpPort);
        peerListener.Start();
        var peerTask = Task.Run(async () =>
        {
            using var client = await peerListener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var line = await BoundedLineReader.ReadLineAsync(stream, CancellationToken.None);
            Assert.NotNull(line);
            Assert.True(SecureEnvelopeCodec.TryParse(line!, out var requestEnvelope));
            var request = SecureEnvelopeCodec.TryOpen<ConfigSyncPullRequestMessage>(requestEnvelope!, groupKeyBase64, sharedKeyPair.PublicKeyBase64, DateTimeOffset.UtcNow);
            Assert.NotNull(request);
            Assert.Equal(ownDeviceId, request!.RequesterDeviceId);

            var response = new ConfigSyncPullResponseMessage(customerGroupId, new SharedConfig { ConfigVersion = 5 });
            var responseEnvelope = SecureEnvelopeCodec.Seal(response, customerGroupId, peerDeviceId, groupKeyBase64, sharedKeyPair.PrivateKeyBase64, DateTimeOffset.UtcNow);
            Assert.NotNull(responseEnvelope);
            var responseBytes = NetworkSerializer.Encoding.GetBytes(JsonSerializer.Serialize(responseEnvelope, WireOptions) + "\n");
            await stream.WriteAsync(responseBytes);
        });

        var deviceList = new List<DeviceEntry>
        {
            new()
            {
                DeviceId = peerDeviceId,
                IpAddress = "127.0.0.1",
                ProtocolVersion = AppConstants.CurrentProtocolVersion,
                PinnedDeviceIdentityPublicKeyBase64 = sharedKeyPair.PublicKeyBase64,
            },
        };

        var appliedSignal = new TaskCompletionSource<SharedConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var configSync = new ConfigSyncService(() => ownIdentity, () => deviceList, groupKeyProvider: () => groupKeyBase64);
        configSync.ConfigApplied += (_, config) => appliedSignal.TrySetResult(config);

        configSync.OnPeerConfigVersionObserved(null, new PeerConfigVersionInfo { DeviceId = peerDeviceId, ConfigVersion = 5 });

        var applied = await appliedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(5, applied.ConfigVersion);

        Assert.Equal(5, new SettingsStore().Load().AppliedConfigVersion);
        await peerTask.WaitAsync(TimeSpan.FromSeconds(5));
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
    public void AlarmRequestMessage_RoundTrips_ThroughWireFormat_WhenIsTestTrue()
    {
        var original = new AlarmRequestMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Sachbearbeitung 3", "Frau Meier", "Zimmer 214", "214", false,
            "Bitte kommen!", 1, DateTimeOffset.UtcNow, IsTest: true);

        var json = JsonSerializer.Serialize(original, WireOptions);
        var roundTripped = JsonSerializer.Deserialize<AlarmRequestMessage>(json, WireOptions);

        Assert.Equal(original, roundTripped);
        Assert.True(roundTripped!.IsTest);
    }

    [Fact]
    public void AlarmRequestMessage_Deserializes_WithMissingIsTestField_AsFalse()
    {
        // Simuliert einen alten Sender ohne das Feld (Rollout-Übergang) - der Fallback muss
        // sicher in Richtung "kein Test" gehen, nie fälschlich einen echten Alarm als Test markieren.
        var jsonWithoutIsTest = """
            {"customerGroupId":"11111111-1111-1111-1111-111111111111","alarmProfileId":"22222222-2222-2222-2222-222222222222","alarmSessionId":"33333333-3333-3333-3333-333333333333","senderDeviceId":"44444444-4444-4444-4444-444444444444","senderComputerName":"PC","senderUser":"User","senderRoomName":"Raum","senderRoomNumber":"1","senderIsRemoteSession":false,"text":"Alarm!","responseThreshold":1,"sentAtUtc":"2026-08-13T12:00:00Z"}
            """;

        var result = JsonSerializer.Deserialize<AlarmRequestMessage>(jsonWithoutIsTest, WireOptions);

        Assert.NotNull(result);
        Assert.False(result!.IsTest);
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

    [Fact]
    public void BootCallMessage_RoundTrips_WithProtocolVersionAndDeviceIdentityKey()
    {
        // LAN-Verschlüsselung (SecureEnvelopeCodec): beide Felder werden nur bei direktem
        // Boot-Call-Kontakt gepinnt/ausgewertet, müssen aber schon auf Wire-Ebene
        // verlustfrei durchgereicht werden.
        var original = new BootCallMessage(
            MessageKind.Reply, Guid.NewGuid(), Guid.NewGuid(), "PC-217", "Herr Novak", "Zimmer 108", "108",
            Role.Admin, true, 51501, "0.30.0", 3, DateTimeOffset.UtcNow,
            ProtocolVersion: AppConstants.CurrentProtocolVersion,
            DeviceIdentityPublicKeyBase64: Convert.ToBase64String(new byte[32]));

        var json = JsonSerializer.Serialize(original, WireOptions);
        var roundTripped = JsonSerializer.Deserialize<BootCallMessage>(json, WireOptions);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void BootCallMessage_Deserializes_WithMissingProtocolFields_AsNull()
    {
        // Simuliert einen alten, vor-verschlüsselungsfähigen Sender (Rollout-Übergang) -
        // "Feld fehlt" muss von "Feld ist gesetzt" unterscheidbar bleiben, deshalb
        // ProtocolVersion bewusst int? statt eines defaultenden int (siehe Klassendoku).
        // kind/role als Zahl, nicht als String: WireOptions setzt nur PropertyNamingPolicy,
        // kein JsonStringEnumConverter - Enums serialisieren hier wie überall im Projekt
        // als Zahl (Announce=0, User=0).
        var jsonWithoutNewFields = """
            {"kind":0,"customerGroupId":"11111111-1111-1111-1111-111111111111","deviceId":"22222222-2222-2222-2222-222222222222","computerName":"PC","user":"User","roomName":"Raum","roomNumber":"1","role":0,"isRemoteSession":false,"tcpPort":51501,"programVersion":"0.29.1","configVersion":0,"sentAtUtc":"2026-08-13T12:00:00Z"}
            """;

        var result = JsonSerializer.Deserialize<BootCallMessage>(jsonWithoutNewFields, WireOptions);

        Assert.NotNull(result);
        Assert.Null(result!.ProtocolVersion);
        Assert.Null(result.DeviceIdentityPublicKeyBase64);
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
