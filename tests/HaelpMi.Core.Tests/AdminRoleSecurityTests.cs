using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Security;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Covers das vierte kryptografische Schlüsselpaar (Ed25519, Admin-Rollen-Signatur,
/// Nutzerwunsch 15.08.2026, CLAUDE.md "Lizenz &amp; Secrets" Punkt 4): AdminRoleSigner/
/// AdminRoleVerifier (reine Kryptologik), DiscoveryService-Integration (direkter Kontakt
/// setzt DeviceEntry.AdminVerified, Gossip nie), und die darauf aufbauenden
/// Vertrauensentscheidungen in EditLockService/AuditSyncService.
/// </summary>
public class AdminRoleSecurityTests
{
    private static readonly JsonSerializerOptions WireOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static LiveIdentity MakeIdentity(Guid customerGroupId, Guid deviceId, Role role = Role.User, string? adminRolePublicKeyBase64 = null) =>
        new(customerGroupId, deviceId, "PC", "User", "Raum", "1", role, false, "9.9.9", 0, adminRolePublicKeyBase64);

    private static int GetFreeUdpPort()
    {
        using var socket = new UdpClient(0);
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ---------------------------------------------------------------------
    // AdminRoleSigner / AdminRoleVerifier: reine Kryptologik
    // ---------------------------------------------------------------------

    [Fact]
    public void SignAndVerify_RoundTrips_WithMatchingKeyPair()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;

        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, deviceId, sentAtUtc);

        Assert.NotNull(signature);
        Assert.True(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void TrySign_ReturnsNull_WhenRoleIsNotAdmin()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();

        var signature = AdminRoleSigner.TrySign(Role.User, Guid.NewGuid(), keyPair.PrivateKeyBase64, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Null(signature); // ein User-Gerät kann selbst nie eine gültige Admin-Behauptung erzeugen
    }

    [Fact]
    public void TrySign_ReturnsNull_WhenNoPrivateKeyPresent()
    {
        var signature = AdminRoleSigner.TrySign(Role.Admin, Guid.NewGuid(), null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Null(signature);
    }

    [Fact]
    public void Verify_Rejects_WhenPublicKeyDoesNotMatch()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var wrongKeyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, deviceId, sentAtUtc);

        Assert.False(AdminRoleVerifier.Verify(wrongKeyPair.PublicKeyBase64, customerGroupId, deviceId, sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void Verify_Rejects_WhenDeviceIdWasSwapped()
    {
        // Eine gültige Signatur für Gerät A darf nicht auch für Gerät B gelten - genau das
        // verhindert, dass DeviceId Teil der signierten Nutzlast ist (AdminRoleClaim).
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, Guid.NewGuid(), sentAtUtc);

        Assert.False(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, Guid.NewGuid() /* andere DeviceId */, sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void Verify_Rejects_WhenCustomerGroupIdWasSwapped()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = AdminRoleSigner.TrySign(Role.Admin, Guid.NewGuid(), keyPair.PrivateKeyBase64, deviceId, sentAtUtc);

        Assert.False(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, Guid.NewGuid() /* andere Kunden-Gruppe */, deviceId, sentAtUtc, signature, sentAtUtc));
    }

    [Fact]
    public void Verify_Rejects_WhenSignatureIsTooOld_ReplayProtection()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow - AdminRoleVerifier.MaxAge - TimeSpan.FromMinutes(1);
        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, deviceId, sentAtUtc);

        Assert.False(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, sentAtUtc, signature, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Verify_Rejects_WhenSignatureIsFromTheFuture()
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var customerGroupId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5);
        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, deviceId, sentAtUtc);

        Assert.False(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, deviceId, sentAtUtc, signature, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein-gueltiges-base64!!!")]
    public void Verify_Rejects_MalformedOrMissingSignature(string? signature)
    {
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var now = DateTimeOffset.UtcNow;

        Assert.False(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, Guid.NewGuid(), Guid.NewGuid(), now, signature, now));
    }

    // ---------------------------------------------------------------------
    // DiscoveryService: direkter Kontakt setzt AdminVerified, Gossip nie
    // ---------------------------------------------------------------------

    [Fact]
    public async Task DiscoveryService_Reply_IsSignedWithOwnAdminPrivateKey_WhenOwnDeviceIsAdmin()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, Role.Admin, keyPair.PublicKeyBase64);

        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort, adminRolePrivateKeyProvider: () => keyPair.PrivateKeyBase64);
        discovery.StartListening();

        using var peerSocket = new UdpClient(0) { EnableBroadcast = true };
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, Guid.NewGuid(), "PC-Peer", "User", "Raum", "1",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);

        var replyListenTask = peerSocket.ReceiveAsync();
        await peerSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        var replyResult = await replyListenTask.WaitAsync(TestTimeout);
        var reply = JsonSerializer.Deserialize<BootCallMessage>(replyResult.Buffer, WireOptions);

        Assert.NotNull(reply);
        Assert.NotNull(reply!.AdminRoleSignatureBase64);
        Assert.True(AdminRoleVerifier.Verify(keyPair.PublicKeyBase64, customerGroupId, ownDeviceId, reply.SentAtUtc, reply.AdminRoleSignatureBase64, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task DiscoveryService_DirectContact_ValidAdminSignature_SetsAdminVerified_AndFiresMeshEvent()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var ownDeviceId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, ownDeviceId, Role.Admin, keyPair.PublicKeyBase64);

        AdminPeerContactInfo? observed = null;
        var eventSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.AdminPeerContactObserved += (_, info) => { observed = info; eventSignal.TrySetResult(); };
        discovery.StartListening();

        var peerDeviceId = Guid.NewGuid();
        var sentAtUtc = DateTimeOffset.UtcNow;
        var signature = AdminRoleSigner.TrySign(Role.Admin, customerGroupId, keyPair.PrivateKeyBase64, peerDeviceId, sentAtUtc);
        using var peerSocket = new UdpClient(0) { EnableBroadcast = true };
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, peerDeviceId, "PC-Peer", "User", "Raum", "1",
            Role.Admin, false, 51999, "9.9.9", 0, sentAtUtc, null, signature);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);
        await peerSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await eventSignal.Task.WaitAsync(TestTimeout);
        Assert.NotNull(observed);
        Assert.Equal(peerDeviceId, observed!.Peer.DeviceId);
        Assert.True(observed.Peer.AdminVerified);

        var stored = new DeviceStore().Load().First(d => d.DeviceId == peerDeviceId);
        Assert.True(stored.AdminVerified);
    }

    [Fact]
    public async Task DiscoveryService_DirectContact_AdminRoleWithoutSignature_DoesNotSetAdminVerified_NorFireMeshEvent()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var keyPair = AdminRoleSigner.GenerateKeyPair();
        var ownIdentity = MakeIdentity(customerGroupId, Guid.NewGuid(), Role.Admin, keyPair.PublicKeyBase64);

        var meshEventFired = false;
        var updatedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.AdminPeerContactObserved += (_, _) => meshEventFired = true;
        discovery.DeviceUpdated += (_, _) => updatedSignal.TrySetResult();
        discovery.StartListening();

        // Peer behauptet Role.Admin, liefert aber KEINE Signatur mit - unauthentifizierte
        // Selbstauskunft, genau der Fall, den diese Änderung schließt.
        var peerDeviceId = Guid.NewGuid();
        using var peerSocket = new UdpClient(0) { EnableBroadcast = true };
        var announce = new BootCallMessage(
            MessageKind.Announce, customerGroupId, peerDeviceId, "PC-Peer", "User", "Raum", "1",
            Role.Admin, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow);
        var announceBytes = JsonSerializer.SerializeToUtf8Bytes(announce, WireOptions);
        await peerSocket.SendAsync(announceBytes, announceBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await updatedSignal.Task.WaitAsync(TestTimeout);
        Assert.False(meshEventFired);

        var stored = new DeviceStore().Load().First(d => d.DeviceId == peerDeviceId);
        Assert.False(stored.AdminVerified);
    }

    [Fact]
    public async Task DiscoveryService_GossipLearnedAdminClaim_NeverSetsAdminVerified()
    {
        using var scope = new TestAppDataScope();
        var discoveryPort = GetFreeUdpPort();
        var customerGroupId = Guid.NewGuid();
        var ownIdentity = MakeIdentity(customerGroupId, Guid.NewGuid());

        var gossipedDeviceId = Guid.NewGuid();
        var updatedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var discovery = new DiscoveryService(() => ownIdentity, discoveryPort: discoveryPort);
        discovery.DeviceUpdated += (_, entry) =>
        {
            if (entry.DeviceId == gossipedDeviceId)
            {
                updatedSignal.TrySetResult();
            }
        };
        discovery.StartListening();

        // KnownDeviceSummary hat gar kein Signaturfeld (bewusste Einschränkung, siehe
        // BootCallMessage-Klassendoku) - Role.Admin über Gossip bleibt daher immer
        // unverifiziert, unabhängig vom Inhalt.
        using var replierSocket = new UdpClient(0) { EnableBroadcast = true };
        var reply = new BootCallMessage(
            MessageKind.Reply, customerGroupId, Guid.NewGuid(), "PC-Antwortend", "User", "Raum", "1",
            Role.User, false, 51999, "9.9.9", 0, DateTimeOffset.UtcNow,
            new List<KnownDeviceSummary> { new(gossipedDeviceId, "PC-Weitweg", "User", "Raum", "2", Role.Admin, "192.168.1.77", 51501, DateTimeOffset.UtcNow) });
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, WireOptions);
        await replierSocket.SendAsync(replyBytes, replyBytes.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

        await updatedSignal.Task.WaitAsync(TestTimeout);

        var stored = new DeviceStore().Load().First(d => d.DeviceId == gossipedDeviceId);
        Assert.Equal(Role.Admin, stored.Role); // die Rollen-Selbstauskunft wird übernommen (Anzeige)...
        Assert.False(stored.AdminVerified); // ...aber nie als verifiziert gewertet (Sicherheitsentscheidung)
    }

    // ---------------------------------------------------------------------
    // EditLockService: Bonus-Prüfung auf der Empfangsseite
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EditLockService_HandleIncomingRequest_DropsRequest_FromUnverifiedRequester_WhenDeviceListProviderSet()
    {
        var customerGroupId = Guid.NewGuid();
        var requesterDeviceId = Guid.NewGuid();
        var knownDevices = new List<DeviceEntry> { new() { DeviceId = requesterDeviceId, Role = Role.Admin, AdminVerified = false } };
        var port = GetFreeTcpPort();

        var service = new EditLockService(() => MakeIdentity(customerGroupId, Guid.NewGuid()), deviceListProvider: () => knownDevices);
        service.Start(port);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();

            var request = new EditLockRequestMessage(customerGroupId, EditScopeKind.Group, Guid.NewGuid(), requesterDeviceId, "PC", "User", DateTimeOffset.UtcNow);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, WireOptions) + "\n");
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();

            using var readCts = new CancellationTokenSource(TestTimeout);
            var line = await BoundedLineReader.ReadLineAsync(stream, readCts.Token);
            Assert.Null(line); // keine Antwort statt fälschlich "Granted"
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task EditLockService_HandleIncomingRequest_Grants_VerifiedRequester_WhenDeviceListProviderSet()
    {
        var customerGroupId = Guid.NewGuid();
        var requesterDeviceId = Guid.NewGuid();
        var knownDevices = new List<DeviceEntry> { new() { DeviceId = requesterDeviceId, Role = Role.Admin, AdminVerified = true } };
        var port = GetFreeTcpPort();

        var service = new EditLockService(() => MakeIdentity(customerGroupId, Guid.NewGuid()), deviceListProvider: () => knownDevices);
        service.Start(port);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();

            var request = new EditLockRequestMessage(customerGroupId, EditScopeKind.Group, Guid.NewGuid(), requesterDeviceId, "PC", "User", DateTimeOffset.UtcNow);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, WireOptions) + "\n");
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();

            using var readCts = new CancellationTokenSource(TestTimeout);
            var line = await BoundedLineReader.ReadLineAsync(stream, readCts.Token);
            Assert.NotNull(line);
            var response = JsonSerializer.Deserialize<EditLockResponseMessage>(line!, WireOptions);
            Assert.True(response!.Granted);
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task EditLockService_HandleIncomingRequest_PreservesOldBehavior_WhenNoDeviceListProviderSet()
    {
        // Rückwärtskompatibilität: ohne Provider (z. B. ältere Aufrufer) unverändertes
        // Verhalten - jede Anfrage wird wie zuvor beantwortet, keine Prüfung.
        var customerGroupId = Guid.NewGuid();
        var port = GetFreeTcpPort();

        var service = new EditLockService(() => MakeIdentity(customerGroupId, Guid.NewGuid()));
        service.Start(port);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();

            var request = new EditLockRequestMessage(customerGroupId, EditScopeKind.Group, Guid.NewGuid(), Guid.NewGuid(), "PC", "User", DateTimeOffset.UtcNow);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, WireOptions) + "\n");
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();

            using var readCts = new CancellationTokenSource(TestTimeout);
            var line = await BoundedLineReader.ReadLineAsync(stream, readCts.Token);
            Assert.NotNull(line);
            var response = JsonSerializer.Deserialize<EditLockResponseMessage>(line!, WireOptions);
            Assert.True(response!.Granted);
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------
    // AuditSyncService: Digest-Empfangsseite lehnt nicht verifizierte Requester ab
    // ---------------------------------------------------------------------

    [Fact]
    public async Task AuditSyncService_HandleIncomingDigest_DropsRequest_FromUnverifiedRequester()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = Diagnostics.SharedLogPaths.ForceLocalFallbackForTests();
        var pushPort = GetFreeTcpPort();
        var meshPort = GetFreeTcpPort();
        var customerGroupId = Guid.NewGuid();
        var requesterDeviceId = Guid.NewGuid();
        var knownDevices = new List<DeviceEntry> { new() { DeviceId = requesterDeviceId, Role = Role.Admin, AdminVerified = false } };

        var service = new AuditSyncService(() => MakeIdentity(customerGroupId, Guid.NewGuid(), Role.Admin), new AuditLog(() => Guid.NewGuid()), deviceListProvider: () => knownDevices);
        service.StartListening(pushPort, meshPort);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, meshPort);
            await using var stream = client.GetStream();

            var request = new AuditDigestRequestMessage(customerGroupId, requesterDeviceId, new Dictionary<Guid, long>());
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, WireOptions) + "\n");
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();

            using var readCts = new CancellationTokenSource(TestTimeout);
            var line = await BoundedLineReader.ReadLineAsync(stream, readCts.Token);
            Assert.Null(line); // keine gesammelten Audit-Daten an ein nicht verifiziertes Gerät
        }
        finally
        {
            await service.DisposeAsync();
        }
    }
}
