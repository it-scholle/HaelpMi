using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Storage;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>Covers Teil 2, Abschnitt 6/9: Kunden-Gruppen-ID-Filterung, Boot-Call, Alarm-Wire-Format.</summary>
public class NetworkingTests
{
    private static readonly JsonSerializerOptions WireOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Für DiscoveryService_PeerWithLicense_RaisesPeerLicenseObserved unten - gleiches
    // Signier-Muster wie LicenseWarningAndImportTests.GenerateTestKeyPair/SignLicense,
    // hier dupliziert statt geteilt (keine gemeinsame private Test-Infrastruktur zwischen
    // Testklassen in diesem Repo).
    private static string MakeSignedLicenseKeyText(Guid customerGroupId)
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;

        var unsigned = new License(customerGroupId, LicenseTier.Custom, 2, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1), SignatureBase64: string.Empty);
        var payload = unsigned.GetSigningPayload();
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        var signed = unsigned with { SignatureBase64 = Convert.ToBase64String(signer.GenerateSignature()) };
        return LicenseKeyText.Encode(signed);
    }

    private static LiveIdentity MakeIdentity(Guid customerGroupId, Guid deviceId, string computerName = "PC", string user = "User", string room = "Raum", string roomNumber = "1") =>
        new(customerGroupId, deviceId, computerName, user, room, roomNumber, Role.User, false, "9.9.9", 0, DateTimeOffset.UtcNow);

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
    public async Task AlarmTcpListener_OwnDeviceLicenseDisabled_ClosesConnection_WithoutReadingOrAcking()
    {
        // Issue #59/#60: ein lizenzüberschrittenes Gerät muss für andere komplett
        // unerreichbar wirken - keine Verarbeitung, kein Ack, nicht nur eine unterdrückte
        // Anzeige.
        var receiverDeviceId = Guid.NewGuid();
        var receiverIdentity = MakeIdentity(Guid.NewGuid(), receiverDeviceId);
        var listener = new AlarmTcpListener(() => receiverIdentity, isOwnDeviceLicenseDisabled: () => true);

        var received = false;
        listener.AlarmReceived += (_, _) => received = true;

        var port = GetFreeTcpPort();
        listener.Start(port);

        try
        {
            var sender = new AlarmSender();
            var target = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = port };
            var identity = MakeIdentity(Guid.NewGuid(), Guid.NewGuid());
            var profile = new AlarmProfile { Text = "Test" };

            var result = await sender.SendAsync(profile, Guid.NewGuid(), identity, new[] { target });

            Assert.Equal(0, result.AckedCount);
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

    // --- Issue #61-Nachtrag (Propagierungs-Bugfix 08.09.2026, Nutzerbericht "Deaktivieren/
    // Aktivieren im Geräte-Tab hat keine Wirkung"): der bisherige blanke "Gossip über mich
    // selbst wird ignoriert"-Filter hat versehentlich auch die einzige legitime
    // Fremdmeinung über die eigene DeviceId verworfen - den Override. ---

    [Fact]
    public async Task DiscoveryService_ReplyToAnnounce_IncludesAnnouncersOwnOverride_WhenSet()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Empfang EG", "Poststelle", "Empfangshalle", "0");

        var newDeviceId = Guid.NewGuid();
        var overrideSetAt = DateTimeOffset.UtcNow;
        // Dieses Gerät kennt den Announcer bereits mit einer im Geräte-Tab getroffenen
        // Deaktivieren-Entscheidung - genau die Information, die beim Announcer selbst
        // ankommen muss.
        new DeviceStore().Save(new List<DeviceEntry>
        {
            new()
            {
                DeviceId = newDeviceId, ComputerName = "PC-NEU", RoomName = "Empfang", RoomNumber = "1",
                LicenseOverride = LicenseOverride.ForceDisabled, LicenseOverrideSetAtUtc = overrideSetAt,
            },
        });

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.StartListening();

        using var newDeviceSocket = new UdpClient(0) { EnableBroadcast = true };
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
        // Anders als DiscoveryService_ReplyToAnnounce_IncludesKnownDevices (dort ohne
        // Override): der Announcer bekommt sich selbst NUR wegen des gesetzten Overrides
        // zurückgemeldet.
        var ownEntry = Assert.Single(reply.KnownDevices!, d => d.DeviceId == newDeviceId);
        Assert.Equal(LicenseOverride.ForceDisabled, ownEntry.Override);
        Assert.Equal(overrideSetAt, ownEntry.OverrideSetAtUtc);
    }

    [Fact]
    public async Task DiscoveryService_GossipAboutSelf_RaisesOwnLicenseOverrideObserved_ButNeverStoresSelfAsPeer()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Neu-PC", "Herr Neu", "Empfang", "1");

        OwnLicenseOverrideInfo? observed = null;
        var observedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.OwnLicenseOverrideObserved += (_, info) =>
        {
            observed = info;
            observedSignal.TrySetResult();
        };
        discovery.StartListening();

        var replierDeviceId = Guid.NewGuid();
        var overrideSetAt = DateTimeOffset.UtcNow;
        using var replierSocket = new UdpClient(0) { EnableBroadcast = true };
        var reply = new BootCallMessage(
            MessageKind.Reply, customerGroupId, replierDeviceId, "Admin-PC", "Admin", "Büro", "5",
            Role.Admin, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow,
            new List<KnownDeviceSummary> { new(ownDeviceId, "Neu-PC", "Herr Neu", "Empfang", "1", Role.User, "127.0.0.1", 51501, null, LicenseOverride.ForceEnabled, overrideSetAt) });
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, WireOptions);

        await replierSocket.SendAsync(replyBytes, replyBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await observedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(observed);
        Assert.Equal(LicenseOverride.ForceEnabled, observed!.Override);
        Assert.Equal(overrideSetAt, observed.SetAtUtc);

        // Architekturregel unverändert: ein Gerät trägt sich nie selbst in seine eigene
        // Peer-Liste ein, auch nicht über einen Umweg durch Gossip.
        var devices = new DeviceStore().Load();
        Assert.DoesNotContain(devices, d => d.DeviceId == ownDeviceId);
    }

    // --- Issue #61-Nachtrag (Fehlerbericht "Löschen im Geräte-Tab deaktiviert das Gerät
    // nicht wirklich - es kann weiter propagieren und Alarme senden"): "Löschen" trug
    // bisher (siehe DeviceStore.Remove) keinerlei Tombstone weiter - ein gelöschtes Gerät,
    // das (von seiner eigenen Löschung nichts ahnend) weiter announct, wurde beim nächsten
    // Boot-Call einfach wieder unsichtbar aufgenommen. RemovedDeviceStore schließt das. ---

    [Fact]
    public async Task DiscoveryService_TombstonedSender_IsNeverResurrected_ButStillGetsAReply()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Admin-PC", "Admin", "Büro", "5");

        var removedDeviceId = Guid.NewGuid();
        new RemovedDeviceStore().Save(new List<RemovedDeviceEntry> { new(removedDeviceId, DateTimeOffset.UtcNow.AddMinutes(-1)) });

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.StartListening();

        using var removedDeviceSocket = new UdpClient(0) { EnableBroadcast = true };
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, removedDeviceId, "PC-Gelöscht", "Herr Weg", "Lager", "9",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);

        var replyListenTask = removedDeviceSocket.ReceiveAsync();
        await removedDeviceSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        // Der Absender weiß von seiner eigenen Löschung noch nichts - er bekommt trotzdem
        // ganz normal eine Antwort (mit seinem eigenen Tombstone darin, siehe nächster
        // Test), nur eben ohne dabei wieder in unsere Geräteliste aufgenommen zu werden.
        var replyResult = await replyListenTask.WaitAsync(TimeSpan.FromSeconds(5));
        var reply = JsonSerializer.Deserialize<BootCallMessage>(replyResult.Buffer, WireOptions);
        Assert.NotNull(reply);

        var devices = new DeviceStore().Load();
        Assert.DoesNotContain(devices, d => d.DeviceId == removedDeviceId);
    }

    [Fact]
    public async Task DiscoveryService_ReplyToAnnounce_IncludesAnnouncersOwnTombstone_WhenRemoved()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Admin-PC", "Admin", "Büro", "5");

        var removedDeviceId = Guid.NewGuid();
        var removedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        new RemovedDeviceStore().Save(new List<RemovedDeviceEntry> { new(removedDeviceId, removedAt) });

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.StartListening();

        using var removedDeviceSocket = new UdpClient(0) { EnableBroadcast = true };
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, removedDeviceId, "PC-Gelöscht", "Herr Weg", "Lager", "9",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);

        var replyListenTask = removedDeviceSocket.ReceiveAsync();
        await removedDeviceSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        var replyResult = await replyListenTask.WaitAsync(TimeSpan.FromSeconds(5));
        var reply = JsonSerializer.Deserialize<BootCallMessage>(replyResult.Buffer, WireOptions);

        // Bugfix 10.09.2026: läuft seitdem über das eigene PermanentlyRemovedDevices-Feld,
        // nicht mehr als getarnter KnownDeviceSummary-Eintrag - siehe
        // PermanentlyRemovedDeviceSummary für die Begründung.
        Assert.NotNull(reply);
        Assert.NotNull(reply!.PermanentlyRemovedDevices);
        var ownTombstone = Assert.Single(reply.PermanentlyRemovedDevices!, t => t.DeviceId == removedDeviceId);
        Assert.Equal(removedAt, ownTombstone.RemovedAtUtc);
    }

    [Fact]
    public async Task DiscoveryService_GossipAboutSelf_Removed_RaisesOwnDeviceRemovedObserved()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "PC-Betroffen", "Herr Betroffen", "Empfang", "1");

        OwnDeviceRemovedInfo? observed = null;
        var observedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.OwnDeviceRemovedObserved += (_, info) =>
        {
            observed = info;
            observedSignal.TrySetResult();
        };
        discovery.StartListening();

        var adminDeviceId = Guid.NewGuid();
        var removedAt = DateTimeOffset.UtcNow;
        using var adminSocket = new UdpClient(0) { EnableBroadcast = true };
        var reply = new BootCallMessage(
            MessageKind.Reply, customerGroupId, adminDeviceId, "Admin-PC", "Admin", "Büro", "5",
            Role.Admin, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow,
            new List<KnownDeviceSummary> { new(ownDeviceId, "", "", "", "", Role.User, "", 0, null, LicenseOverride.None, null, Removed: true, RemovedSetAtUtc: removedAt) });
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, WireOptions);

        await adminSocket.SendAsync(replyBytes, replyBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await observedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(observed);
        Assert.True(observed!.Removed);
        Assert.Equal(removedAt, observed.SetAtUtc);

        // Wie beim Override: ein Gerät trägt sich nie selbst in seine eigene Peer-Liste ein.
        var devices = new DeviceStore().Load();
        Assert.DoesNotContain(devices, d => d.DeviceId == ownDeviceId);
    }

    [Fact]
    public async Task DiscoveryService_GossipReportsThirdPartyRemoved_MarksEntryButKeepsItVisible()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Admin-B-PC", "Admin B", "Büro", "6");

        // Dieses Admin-Dashboard kennt das deinstallierte Gerät noch ganz normal - es hat
        // die Deinstallations-Meldung auf einem ANDEREN Admin-Gerät nie gesehen.
        var removedDeviceId = Guid.NewGuid();
        new DeviceStore().Save(new List<DeviceEntry> { new() { DeviceId = removedDeviceId, ComputerName = "PC-Weg", RoomName = "Lager", RoomNumber = "9" } });

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.StartListening();

        var adminADeviceId = Guid.NewGuid();
        var removedAt = DateTimeOffset.UtcNow;
        using var adminASocket = new UdpClient(0) { EnableBroadcast = true };
        var reply = new BootCallMessage(
            MessageKind.Reply, customerGroupId, adminADeviceId, "Admin-A-PC", "Admin A", "Büro", "5",
            Role.Admin, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow,
            new List<KnownDeviceSummary> { new(removedDeviceId, "PC-Weg", "", "Lager", "9", Role.User, "192.168.1.40", 51501, null, LicenseOverride.None, null, Removed: true, RemovedSetAtUtc: removedAt) });
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, WireOptions);

        var updatedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        discovery.DeviceUpdated += (_, _) => updatedSignal.TrySetResult(); // vom Absender adminADeviceId, nicht vom Tombstone-Eintrag

        await adminASocket.SendAsync(replyBytes, replyBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));
        await updatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Issue #61-Nachtrag ("Deinstalliert" statt Verstecken): das Gerät bleibt SICHTBAR
        // in der Übersicht, nur markiert - anders als beim endgültigen Löschen landet hier
        // nichts im RemovedDeviceStore.
        var devices = new DeviceStore().Load();
        var marked = Assert.Single(devices, d => d.DeviceId == removedDeviceId);
        Assert.True(marked.Removed);
        Assert.Equal(removedAt, marked.RemovedSetAtUtc);

        var removedDevices = new RemovedDeviceStore().Load();
        Assert.False(RemovedDeviceStore.Contains(removedDevices, removedDeviceId));
    }

    // --- Bugfix "gelöschtes Gerät taucht per Gossip wieder auf" 10.09.2026: ein per
    // "Endgültig löschen" gesetzter Tombstone (PermanentlyRemovedDeviceSummary) muss sich
    // über einen dritten Peer, der den ursprünglichen Löschklick nie gesehen hat, weiter
    // durchsetzen - sonst lebt das (weiterhin laufende) Gerät bei jedem Peer wieder auf,
    // der es direkt kontaktiert, bevor er den Tombstone erhalten hat. ---

    [Fact]
    public async Task DiscoveryService_ReceivesPermanentTombstoneViaGossip_AddsItLocally_AndSenderIsNeverResurrected()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Admin-B-PC", "Admin B", "Büro", "6");

        // Dieses Admin-Dashboard hat "Endgültig löschen" nie selbst geklickt - es kennt
        // das gelöschte Gerät bislang gar nicht.
        var removedDeviceId = Guid.NewGuid();
        var removedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.StartListening();

        var adminADeviceId = Guid.NewGuid();
        using var adminASocket = new UdpClient(0) { EnableBroadcast = true };
        var reply = new BootCallMessage(
            MessageKind.Reply, customerGroupId, adminADeviceId, "Admin-A-PC", "Admin A", "Büro", "5",
            Role.Admin, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow,
            PermanentlyRemovedDevices: new List<PermanentlyRemovedDeviceSummary> { new(removedDeviceId, removedAt) });
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, WireOptions);

        var updatedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        discovery.DeviceUpdated += (_, _) => updatedSignal.TrySetResult(); // vom Absender adminADeviceId, nicht vom Tombstone-Eintrag

        await adminASocket.SendAsync(replyBytes, replyBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));
        await updatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var removedDevices = new RemovedDeviceStore().Load();
        Assert.True(RemovedDeviceStore.Contains(removedDevices, removedDeviceId));

        // Das gelöschte Gerät weiß von seiner eigenen Löschung nichts und meldet sich
        // ganz normal direkt bei diesem Peer - es darf trotzdem nicht wieder auftauchen
        // (gleiche Zusicherung wie DiscoveryService_TombstonedSender_IsNeverResurrected_ButStillGetsAReply,
        // nur diesmal mit einem per Gossip GELERNTEN statt lokal gesetzten Tombstone).
        using var removedDeviceSocket = new UdpClient(0) { EnableBroadcast = true };
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, removedDeviceId, "PC-Gelöscht", "Herr Weg", "Lager", "9",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);

        var replyListenTask = removedDeviceSocket.ReceiveAsync();
        await removedDeviceSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));
        await replyListenTask.WaitAsync(TimeSpan.FromSeconds(5));

        var devices = new DeviceStore().Load();
        Assert.DoesNotContain(devices, d => d.DeviceId == removedDeviceId);
    }

    [Fact]
    public async Task DiscoveryService_GossipedTombstone_AboutOwnDeviceId_IsIgnored()
    {
        // Ein Gerät trägt sich nie selbst in seinen eigenen RemovedDeviceStore ein - sonst
        // könnte jeder ungeprüfte Peer (keine Admin-Signaturprüfung auf diesem
        // Versionsstand, CLAUDE.md) die eigene Identität dauerhaft sperren.
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "PC-Betroffen", "Herr Betroffen", "Empfang", "1");

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        var updatedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        discovery.DeviceUpdated += (_, _) => updatedSignal.TrySetResult();
        discovery.StartListening();

        var adminDeviceId = Guid.NewGuid();
        using var adminSocket = new UdpClient(0) { EnableBroadcast = true };
        var reply = new BootCallMessage(
            MessageKind.Reply, customerGroupId, adminDeviceId, "Admin-PC", "Admin", "Büro", "5",
            Role.Admin, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow,
            PermanentlyRemovedDevices: new List<PermanentlyRemovedDeviceSummary> { new(ownDeviceId, DateTimeOffset.UtcNow) });
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, WireOptions);

        await adminSocket.SendAsync(replyBytes, replyBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));
        await updatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5)); // vom Absender adminDeviceId

        var removedDevices = new RemovedDeviceStore().Load();
        Assert.False(RemovedDeviceStore.Contains(removedDevices, ownDeviceId));
    }

    [Fact]
    public async Task DiscoveryService_SelfReport_WithNewerLastInstalledAtUtc_ClearsPreviouslyKnownRemoved()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, "Admin-PC", "Admin", "Büro", "5");

        var reinstalledDeviceId = Guid.NewGuid();
        var removedAt = DateTimeOffset.UtcNow.AddDays(-1);
        new DeviceStore().Save(new List<DeviceEntry>
        {
            new() { DeviceId = reinstalledDeviceId, ComputerName = "PC-Alt", Removed = true, RemovedSetAtUtc = removedAt },
        });

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.StartListening();

        var updatedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        discovery.DeviceUpdated += (_, _) => updatedSignal.TrySetResult();

        using var reinstalledSocket = new UdpClient(0) { EnableBroadcast = true };
        // Selbstbericht (kein KnownDevices-Anhang) mit einem NEUEREN LastInstalledAtUtc als
        // der lokal gespeicherte RemovedSetAtUtc - genau das Signal eines --post-install-Laufs.
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, reinstalledDeviceId, "PC-Neu", "Frau Neu", "Empfang", "1",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow,
            LastInstalledAtUtc: DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);

        await reinstalledSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));
        await updatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var devices = new DeviceStore().Load();
        var entry = Assert.Single(devices, d => d.DeviceId == reinstalledDeviceId);
        Assert.False(entry.Removed);
        Assert.Null(entry.RemovedSetAtUtc);
        Assert.Equal("PC-Neu", entry.ComputerName); // normaler Selbstbericht wurde trotzdem ganz normal übernommen
    }

    // --- Issue #61-Nachtrag 08.09.2026 ("der Admin sollte im besten Fall auch gar nicht
    // händisch deaktivieren müssen"): der Uninstaller meldet die eigene Deinstallation
    // aktiv ans Netz, siehe DiscoveryService.AnnounceSelfRemovedAsync. ---

    [Fact]
    public async Task AnnounceSelfRemovedAsync_MarksSenderAsRemoved_OnReceiverSide_ButKeepsRowVisible()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var uninstalledDeviceId = Guid.NewGuid();
        var uninstalledIdentity = MakeIdentity(customerGroupId, uninstalledDeviceId, "PC-Weg", "Frau Weg", "Lager", "9");

        var receiverDeviceId = Guid.NewGuid();
        var receiverIdentity = MakeIdentity(customerGroupId, receiverDeviceId, "Admin-PC", "Admin", "Büro", "5");

        await using var receiver = new DiscoveryService(() => receiverIdentity, discoveryPort: discoveryPort);
        var updatedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.DeviceUpdated += (_, _) => updatedSignal.TrySetResult();
        receiver.StartListening();

        await using var uninstaller = new DiscoveryService(() => uninstalledIdentity, discoveryPort: discoveryPort);
        uninstaller.StartListening();

        await uninstaller.AnnounceSelfRemovedAsync();
        await updatedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var devices = new DeviceStore().Load();
        var entry = Assert.Single(devices, d => d.DeviceId == uninstalledDeviceId);
        Assert.True(entry.Removed);
        Assert.Equal("PC-Weg", entry.ComputerName); // ganz normaler, sichtbarer Eintrag - nicht versteckt
    }

    // --- Issue #61-Nachtrag (Nutzerbericht 08.09.2026 "Löschen ist ein Freischein" - der
    // Broadcast oben allein hat weder das gelöschte Gerät selbst noch ein zweites
    // Admin-Dashboard zuverlässig erreicht): NotifyKnownPeersDirectlyAsync kontaktiert
    // zusätzlich jede bekannte IP direkt per Unicast. ---

    [Fact]
    public void CollectNotifyTargetIps_CombinesKnownAndRemovedDevices_DedupedAndWithoutEmptyIps()
    {
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = Guid.NewGuid(), IpAddress = "192.168.1.10" },
            new() { DeviceId = Guid.NewGuid(), IpAddress = "" }, // nie erreicht - keine echte IP
            new() { DeviceId = Guid.NewGuid(), IpAddress = "192.168.1.10" }, // Duplikat
        };
        var removedDevices = new List<RemovedDeviceEntry>
        {
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, "192.168.1.20"),
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, null), // beim Löschen keine IP bekannt
        };

        var targets = DiscoveryService.CollectNotifyTargetIps(devices, removedDevices);

        Assert.Equal(new[] { "192.168.1.10", "192.168.1.20" }, targets.OrderBy(x => x));
    }

    [Fact]
    public async Task NotifyKnownPeersDirectlyAsync_UnicastsTombstone_ToRemovedDevicesLastKnownIp()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();

        var adminDeviceId = Guid.NewGuid();
        var adminIdentity = MakeIdentity(customerGroupId, adminDeviceId, "Admin-PC", "Admin", "Büro", "5");

        var removedDeviceId = Guid.NewGuid();
        var removedAt = DateTimeOffset.UtcNow;
        new RemovedDeviceStore().Save(new List<RemovedDeviceEntry> { new(removedDeviceId, removedAt, "127.0.0.1") });

        await using var admin = new DiscoveryService(() => adminIdentity, discoveryPort: discoveryPort);
        admin.StartListening();

        // Simuliert das gelöschte Gerät: an die exakte Loopback-Adresse gebunden (statt
        // 0.0.0.0 wie admin oben) - eine konkrete Bindung gewinnt bei eingehendem Unicast
        // gegenüber einer Wildcard-Bindung auf demselben Port, genau das Verhalten, das
        // NotifyKnownPeersDirectlyAsync in echt zwischen zwei unterschiedlichen Maschinen
        // ausnutzt (dort mit unterschiedlichen echten IPs statt einer geteilten Loopback).
        using var removedDeviceSocket = new UdpClient();
        removedDeviceSocket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        removedDeviceSocket.Client.Bind(new IPEndPoint(IPAddress.Loopback, discoveryPort));

        var receiveTask = removedDeviceSocket.ReceiveAsync();
        await admin.NotifyKnownPeersDirectlyAsync();

        var result = await receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
        var message = JsonSerializer.Deserialize<BootCallMessage>(result.Buffer, WireOptions);

        // Bugfix 10.09.2026: läuft seitdem über das eigene PermanentlyRemovedDevices-Feld,
        // nicht mehr als getarnter KnownDeviceSummary-Eintrag - siehe
        // PermanentlyRemovedDeviceSummary für die Begründung.
        Assert.NotNull(message);
        Assert.NotNull(message!.PermanentlyRemovedDevices);
        var tombstone = Assert.Single(message.PermanentlyRemovedDevices!, t => t.DeviceId == removedDeviceId);
        Assert.Equal(removedAt, tombstone.RemovedAtUtc);
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
    public async Task DiscoveryService_PeerWithLicense_RaisesPeerLicenseObserved()
    {
        // Issue #59/#60-Nachtrag "Lizenz sofort verteilen" (Nutzerbericht 07.09.2026).
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, Guid.NewGuid());
        var licenseKeyText = MakeSignedLicenseKeyText(customerGroupId);

        PeerLicenseInfo? observed = null;
        var observedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.PeerLicenseObserved += (_, info) =>
        {
            observed = info;
            observedSignal.TrySetResult();
        };
        discovery.StartListening();

        using var peerSocket = new UdpClient(0) { EnableBroadcast = true };
        var peerDeviceId = Guid.NewGuid();
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, peerDeviceId, "PC-ADMIN", "Admin", "Leitstelle", "0",
            Role.Admin, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow, LicenseKeyText: licenseKeyText);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);
        await peerSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await observedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(observed);
        Assert.Equal(peerDeviceId, observed!.DeviceId);
        Assert.Equal(licenseKeyText, observed.LicenseKeyText);
    }

    // Regressionsschutz (Fehlerbericht 07.09.2026, "Config kommt nach Lizenz-Freischaltung
    // nicht mehr an"): PeerLicenseObserved stand im Code VOR PeerConfigVersionObserved und
    // der Boot-Call-Antwort - ein werfender Abonnent (HaelpMi.Agent's Lizenz-Übernahme,
    // Datei-I/O + Signaturprüfung) hätte beides für JEDEN Boot-Call verhindert, der eine
    // Lizenz mitbringt, sobald irgendein Gerät im Kreis eine geladen hat.
    [Fact]
    public async Task DiscoveryService_ThrowingPeerLicenseObserver_StillRaisesConfigVersionObserved_AndStillReplies()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, Guid.NewGuid());
        var licenseKeyText = MakeSignedLicenseKeyText(customerGroupId);

        var configVersionObservedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.PeerLicenseObserved += (_, _) => throw new InvalidOperationException("simulierter Abonnenten-Fehler (z. B. Datei gerade gesperrt)");
        discovery.PeerConfigVersionObserved += (_, _) => configVersionObservedSignal.TrySetResult();
        discovery.StartListening();

        using var peerSocket = new UdpClient(0) { EnableBroadcast = true };
        peerSocket.Client.ReceiveTimeout = 5000;
        var peerDeviceId = Guid.NewGuid();
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, peerDeviceId, "PC-ADMIN", "Admin", "Leitstelle", "0",
            Role.Admin, false, 51999, "9.9.9", 5 /* neuer als unsere 0 */, DateTimeOffset.UtcNow, LicenseKeyText: licenseKeyText);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);
        await peerSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        // PeerConfigVersionObserved (steht im Code NACH PeerLicenseObserved) muss trotz des
        // werfenden Lizenz-Abonnenten feuern ...
        await configVersionObservedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // ... und der Announcer muss trotzdem eine direkte Antwort bekommen (sonst lernt er
        // nie von uns, obwohl wir online sind).
        var replyResult = await peerSocket.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var reply = JsonSerializer.Deserialize<BootCallMessage>(replyResult.Buffer, WireOptions);
        Assert.NotNull(reply);
        Assert.Equal(MessageKind.Reply, reply!.Kind);
        Assert.Equal(ownIdentity.DeviceId, reply.DeviceId);
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

    [Fact]
    public async Task ConfigSyncService_NeverPullsOrApplies_WhenOwnDeviceIsRemoved()
    {
        using var scope = new TestAppDataScope();
        var customerGroupId = Guid.NewGuid();
        var ownDeviceId = Guid.NewGuid();

        new SettingsStore().Save(new OwnSettings
        {
            DeviceId = ownDeviceId,
            CustomerGroupId = customerGroupId,
            AppliedConfigVersion = 0,
            Removed = true,
            RemovedSetAtUtc = DateTimeOffset.UtcNow,
        });
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId);

        var peerDeviceId = Guid.NewGuid();
        var deviceList = new List<DeviceEntry> { new() { DeviceId = peerDeviceId, IpAddress = "127.0.0.1" } };

        var applied = false;
        await using var configSync = new ConfigSyncService(() => ownIdentity, () => deviceList);
        configSync.ConfigApplied += (_, _) => applied = true;

        configSync.OnPeerConfigVersionObserved(null, new PeerConfigVersionInfo { DeviceId = peerDeviceId, ConfigVersion = 5 });

        // Kein TCP-Peer für den Pull nötig - das gelöschte Gerät darf gar nicht erst
        // versuchen, eine Verbindung aufzubauen, geschweige denn etwas zu übernehmen.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.False(applied);
        Assert.Equal(0, new SettingsStore().Load().AppliedConfigVersion);
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
