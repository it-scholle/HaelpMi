using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Security;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Covers das revisionssichere Audit-Log (Nutzerwunsch 14./15.08.2026): Hash-Chain
/// (AuditLog), additive Admin-Ablage mit Lückenerkennung (AuditIngestStore), Push-Sync +
/// Admin-Mesh-Abgleich (AuditSyncService), Last-Seen-Gossip (DeviceStore).
/// </summary>
public class AuditSyncTests
{
    private static readonly JsonSerializerOptions WireOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static LiveIdentity MakeIdentity(Guid customerGroupId, Guid deviceId) =>
        new(customerGroupId, deviceId, "PC", "User", "Raum", "1", Role.Admin, false, "9.9.9", 0);

    /// <summary>Baut eine in sich konsistente (echte SHA256-)Kette - direkt für AuditIngestStore-Tests, ohne den Umweg über eine echte AuditLog-Instanz.</summary>
    private static List<AuditLogEntry> MakeChain(Guid originDeviceId, int count, long startSeq = 1, string startPrevHash = "")
    {
        var entries = new List<AuditLogEntry>();
        var prevHash = startPrevHash;
        for (var i = 0; i < count; i++)
        {
            var seq = startSeq + i;
            var timestamp = DateTimeOffset.UtcNow;
            var hash = AuditHashChain.Compute(prevHash, seq, timestamp, $"entry {seq}");
            entries.Add(new AuditLogEntry(seq, originDeviceId, timestamp, prevHash, hash, $"entry {seq}"));
            prevHash = hash;
        }

        return entries;
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
    // AuditLog: Hash-Chain
    // ---------------------------------------------------------------------

    [Fact]
    public void AuditLog_Append_ChainsSequentialEntries()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        var deviceId = Guid.NewGuid();
        var log = new AuditLog(() => deviceId);

        log.Append("erstes Ereignis");
        log.Append("zweites Ereignis");
        log.Append("drittes Ereignis");

        var entries = log.ReadSince(0, 100);
        Assert.Equal(new long[] { 1, 2, 3 }, entries.Select(e => e.Seq));
        Assert.All(entries, e => Assert.Equal(deviceId, e.OriginDeviceId));
        Assert.Equal(string.Empty, entries[0].PrevHash);
        Assert.Equal(entries[0].EntryHash, entries[1].PrevHash);
        Assert.Equal(entries[1].EntryHash, entries[2].PrevHash);
        Assert.NotEqual(entries[0].EntryHash, entries[1].EntryHash);
    }

    [Fact]
    public void AuditLog_ChainState_PersistsAcrossInstances()
    {
        // Simuliert einen Prozess-Neustart - eine neue AuditLog-Instanz muss den Chain-
        // Stand aus der Sidecar-Datei übernehmen statt wieder bei Seq 1/leerem PrevHash zu
        // beginnen (sonst könnten zwei verschiedene Einträge dieselbe Seq tragen).
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        var deviceId = Guid.NewGuid();

        new AuditLog(() => deviceId).Append("vor Neustart");
        var second = new AuditLog(() => deviceId);
        second.Append("nach Neustart");

        var entries = second.ReadSince(0, 100);
        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries[1].Seq);
        Assert.Equal(entries[0].EntryHash, entries[1].PrevHash);
    }

    [Fact]
    public void AuditLog_ReadSince_ReturnsOnlyEntriesAfterGivenSeq_AndRespectsMaxCount()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        var log = new AuditLog(() => Guid.NewGuid());
        for (var i = 0; i < 5; i++)
        {
            log.Append($"entry {i}");
        }

        Assert.Equal(new long[] { 2, 3 }, log.ReadSince(1, 2).Select(e => e.Seq));
        Assert.Equal(new long[] { 1, 2 }, log.ReadSince(0, 2).Select(e => e.Seq));
        Assert.Empty(log.ReadSince(5, 100));
    }

    // ---------------------------------------------------------------------
    // AuditIngestStore: additive Ablage + Lückenerkennung
    // ---------------------------------------------------------------------

    [Fact]
    public void AuditIngestStore_Append_StoresCleanChain_WithoutGap()
    {
        using var appData = new TestAppDataScope();
        var store = new AuditIngestStore();
        var originId = Guid.NewGuid();

        var result = store.Append(originId, MakeChain(originId, 3));

        Assert.Equal(3, result.AcceptedCount);
        Assert.False(result.GapDetected);
        Assert.Equal(3, store.GetHighWaterMarks()[originId]);
    }

    [Fact]
    public void AuditIngestStore_Append_DetectsGap_WhenAnEntryIsMissingBetweenBatches()
    {
        using var appData = new TestAppDataScope();
        var store = new AuditIngestStore();
        var originId = Guid.NewGuid();
        store.Append(originId, MakeChain(originId, 2)); // Seq 1,2

        // Seq 3 wurde nie zugestellt - Seq 4 kommt an, dessen PrevHash auf einen nie
        // gesehenen Seq-3-Hash zeigt.
        var tail = MakeChain(originId, 1, startSeq: 4, startPrevHash: "hash-von-nie-empfangener-seq-3");
        var result = store.Append(originId, tail);

        Assert.True(result.GapDetected);
        Assert.Equal(1, result.AcceptedCount); // wird trotzdem gespeichert, nur markiert (ehrliche Unvollständigkeit statt Verwerfen)
    }

    [Fact]
    public void AuditIngestStore_Append_DetectsTamperedEntry_WhereContentDoesNotMatchItsOwnHash()
    {
        // Eine in sich konsistent WEITERgerechnete, aber gefälschte Kette (Seq/PrevHash
        // passen an, der EntryHash selbst passt aber nicht mehr zu Content+PrevHash+Seq)
        // muss ebenfalls als Lücke/Bruch erkannt werden, nicht nur eine fehlende Seq.
        using var appData = new TestAppDataScope();
        var store = new AuditIngestStore();
        var originId = Guid.NewGuid();
        var entries = MakeChain(originId, 2);
        var tampered = entries[1] with { Content = "manipulierter Inhalt" }; // EntryHash bleibt der alte, unpassende Wert

        var result = store.Append(originId, new[] { entries[0], tampered });

        Assert.True(result.GapDetected);
    }

    [Fact]
    public void AuditIngestStore_Append_IsIdempotent_OnDuplicateDelivery()
    {
        using var appData = new TestAppDataScope();
        var store = new AuditIngestStore();
        var originId = Guid.NewGuid();
        var entries = MakeChain(originId, 3);
        store.Append(originId, entries);

        // Retry (z. B. weil der Sender das Ack nicht mitbekommen hat) liefert dieselben
        // Einträge nochmal - dürfen nicht doppelt gezählt/gespeichert werden.
        var result = store.Append(originId, entries);

        Assert.Equal(0, result.AcceptedCount);
        Assert.False(result.GapDetected);
        Assert.Equal(3, store.GetHighWaterMarks()[originId]);
    }

    [Fact]
    public void AuditIngestStore_GetHighWaterMarks_TracksMultipleOriginDevicesIndependently()
    {
        using var appData = new TestAppDataScope();
        var store = new AuditIngestStore();
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        store.Append(deviceA, MakeChain(deviceA, 5));
        store.Append(deviceB, MakeChain(deviceB, 2));

        var marks = store.GetHighWaterMarks();

        Assert.Equal(5, marks[deviceA]);
        Assert.Equal(2, marks[deviceB]);
    }

    // ---------------------------------------------------------------------
    // AuditSyncStateStore: Zustellstand pro Ziel-Admin
    // ---------------------------------------------------------------------

    [Fact]
    public void AuditSyncStateStore_RoundTrips_PerAdminWatermarks()
    {
        using var appData = new TestAppDataScope();
        var store = new AuditSyncStateStore();
        var adminA = Guid.NewGuid();
        var adminB = Guid.NewGuid();
        var state = store.Load();
        state.LastAckedSeqByAdmin[adminA] = 10;
        state.LastAckedSeqByAdmin[adminB] = 3;
        store.Save(state);

        var reloaded = store.Load();

        Assert.Equal(10, reloaded.LastAckedSeqByAdmin[adminA]);
        Assert.Equal(3, reloaded.LastAckedSeqByAdmin[adminB]);
    }

    [Fact]
    public void AuditSyncStateStore_Load_ReturnsEmptyState_WhenNoFileExistsYet()
    {
        using var appData = new TestAppDataScope();
        var store = new AuditSyncStateStore();

        Assert.Empty(store.Load().LastAckedSeqByAdmin);
    }

    // ---------------------------------------------------------------------
    // DeviceStore: Last-Seen-Gossip (Nutzerwunsch 15.08.2026)
    // ---------------------------------------------------------------------

    [Fact]
    public void Upsert_NewDeviceViaGossip_UsesReportedLastSeenUtc_NotNow()
    {
        var devices = new List<DeviceEntry>();
        var deviceId = Guid.NewGuid();
        var reported = DateTimeOffset.UtcNow.AddHours(-3); // Informant hat es vor 3h zuletzt gesehen
        var info = new DeviceUpsertInfo("PC", "User", "Raum", "1", Role.User, false, "192.168.1.5", 51501, reported);

        DeviceStore.Upsert(devices, deviceId, info, DateTimeOffset.UtcNow);

        Assert.Equal(reported, devices[0].LastSeenUtc);
    }

    [Fact]
    public void Upsert_ExistingDevice_DirectContact_AlwaysUsesNow()
    {
        var deviceId = Guid.NewGuid();
        var devices = new List<DeviceEntry> { new() { DeviceId = deviceId, LastSeenUtc = DateTimeOffset.UtcNow.AddDays(-1) } };
        var now = DateTimeOffset.UtcNow;
        var info = new DeviceUpsertInfo("PC", "User", "Raum", "1", Role.User, false, "192.168.1.5", 51501); // ReportedLastSeenUtc = null = direkter Kontakt

        DeviceStore.Upsert(devices, deviceId, info, now);

        Assert.Equal(now, devices[0].LastSeenUtc);
    }

    [Fact]
    public void Upsert_ExistingDevice_GossipReport_NeverRegressesLastSeenUtc()
    {
        var deviceId = Guid.NewGuid();
        var alreadyKnownNewer = DateTimeOffset.UtcNow.AddMinutes(-5);
        var devices = new List<DeviceEntry> { new() { DeviceId = deviceId, LastSeenUtc = alreadyKnownNewer } };
        var staleGossipReport = DateTimeOffset.UtcNow.AddHours(-2); // ein anderer Informant hat es vor 2h gesehen - älter als unser eigener Stand
        var info = new DeviceUpsertInfo("PC", "User", "Raum", "1", Role.User, false, "192.168.1.5", 51501, staleGossipReport);

        DeviceStore.Upsert(devices, deviceId, info, DateTimeOffset.UtcNow.AddMinutes(-10));

        Assert.Equal(alreadyKnownNewer, devices[0].LastSeenUtc); // nicht rückwärts überschrieben
    }

    // ---------------------------------------------------------------------
    // AuditSyncService: Netzwerk
    // ---------------------------------------------------------------------

    [Fact]
    public async Task AuditSyncService_Start_OnAlreadyOccupiedPorts_DoesNotThrow()
    {
        var pushPort = GetFreeTcpPort();
        var meshPort = GetFreeTcpPort();
        using var occupierPush = new TcpListener(IPAddress.Loopback, pushPort);
        occupierPush.Start();
        using var occupierMesh = new TcpListener(IPAddress.Loopback, meshPort);
        occupierMesh.Start();

        var service = new AuditSyncService(() => MakeIdentity(Guid.NewGuid(), Guid.NewGuid()), new AuditLog(() => Guid.NewGuid()));

        var exception = Record.Exception(() => service.StartListening(pushPort, meshPort));
        Assert.Null(exception);

        await service.DisposeAsync();
    }

    [Fact]
    public async Task AuditSyncService_PushPendingAsync_OnlyContactsAdminPeers()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        var port = GetFreeTcpPort();

        var acceptedConnections = 0;
        using var rawListener = new TcpListener(IPAddress.Loopback, port);
        rawListener.Start();
        using var acceptLoopCts = new CancellationTokenSource();
        var acceptLoop = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await rawListener.AcceptTcpClientAsync(acceptLoopCts.Token);
                    Interlocked.Increment(ref acceptedConnections);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        });

        var customerGroupId = Guid.NewGuid();
        var ownDeviceId = Guid.NewGuid();
        var log = new AuditLog(() => ownDeviceId);
        log.Append("test-ereignis");
        var service = new AuditSyncService(() => MakeIdentity(customerGroupId, ownDeviceId), log);

        var adminPeer = new DeviceEntry { DeviceId = Guid.NewGuid(), Role = Role.Admin, IpAddress = "127.0.0.1", TcpPort = port };
        var userPeer = new DeviceEntry { DeviceId = Guid.NewGuid(), Role = Role.User, IpAddress = "127.0.0.1", TcpPort = port };

        await service.PushPendingAsync(new[] { adminPeer, userPeer }, pushPort: port);
        await Task.Delay(300); // Zeit für eine eventuelle (fälschliche) zweite Verbindung vom User-Peer

        acceptLoopCts.Cancel();
        rawListener.Stop();
        try { await acceptLoop; } catch { /* erwartet nach Cancel/Stop */ }

        Assert.Equal(1, acceptedConnections); // nur der Admin-Peer wurde kontaktiert
    }

    [Fact]
    public async Task AuditSyncService_PushPendingAsync_FullRoundTrip_IngestsAndUpdatesPerAdminState()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        var pushPort = GetFreeTcpPort();
        var meshPort = GetFreeTcpPort();
        var customerGroupId = Guid.NewGuid();
        var adminDeviceId = Guid.NewGuid();

        var adminService = new AuditSyncService(() => MakeIdentity(customerGroupId, adminDeviceId), new AuditLog(() => adminDeviceId));
        adminService.StartListening(pushPort, meshPort);

        try
        {
            var senderDeviceId = Guid.NewGuid();
            var senderLog = new AuditLog(() => senderDeviceId);
            senderLog.Append("alarm gesendet");
            senderLog.Append("alarm beendet");
            var senderService = new AuditSyncService(() => MakeIdentity(customerGroupId, senderDeviceId), senderLog);

            var adminPeer = new DeviceEntry { DeviceId = adminDeviceId, Role = Role.Admin, IpAddress = "127.0.0.1", TcpPort = pushPort };

            await senderService.PushPendingAsync(new[] { adminPeer }, pushPort: pushPort);

            // Admin-Seite: beide Einträge müssen jetzt in AuditIngestStore stehen.
            Assert.Equal(2, new AuditIngestStore().GetHighWaterMarks()[senderDeviceId]);

            // Sender-Seite: Zustellstand für DIESEN Admin steht jetzt auf 2 - ein zweiter
            // Push ohne neue Einträge darf nichts mehr verschicken (Delta, nicht alles).
            var stateAfterFirstPush = new AuditSyncStateStore().Load();
            Assert.Equal(2, stateAfterFirstPush.LastAckedSeqByAdmin[adminDeviceId]);

            senderLog.Append("dritter Eintrag");
            await senderService.PushPendingAsync(new[] { adminPeer }, pushPort: pushPort);
            Assert.Equal(3, new AuditIngestStore().GetHighWaterMarks()[senderDeviceId]);

            await senderService.DisposeAsync();
        }
        finally
        {
            await adminService.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuditSyncService_HandleIncomingPush_DifferentCustomerGroupId_IsSilentlyIgnored()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        var pushPort = GetFreeTcpPort();
        var meshPort = GetFreeTcpPort();
        var ownCustomerGroupId = Guid.NewGuid();
        var responderDeviceId = Guid.NewGuid();

        var service = new AuditSyncService(() => MakeIdentity(ownCustomerGroupId, responderDeviceId), new AuditLog(() => responderDeviceId));
        service.StartListening(pushPort, meshPort);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, pushPort);
            await using var stream = client.GetStream();

            var foreignOriginId = Guid.NewGuid();
            var request = new AuditPushMessage(Guid.NewGuid() /* fremde Kunden-Gruppen-ID */, foreignOriginId, MakeChain(foreignOriginId, 1), DateTimeOffset.UtcNow);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, WireOptions) + "\n");
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();

            // Teil 2, Abschnitt 6: kein Ack, weil der Dienst die Nachricht verwirft, bevor
            // er überhaupt antwortet - Verbindung wird einfach ohne Antwort geschlossen.
            using var readCts = new CancellationTokenSource(TestTimeout);
            var line = await BoundedLineReader.ReadLineAsync(stream, readCts.Token);
            Assert.Null(line);

            Assert.Empty(new AuditIngestStore().GetHighWaterMarks());
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuditSyncService_HandleIncomingDigest_RespondsWithMissingEntries_ForOriginsWhereResponderIsAhead()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        var pushPort = GetFreeTcpPort();
        var meshPort = GetFreeTcpPort();
        var customerGroupId = Guid.NewGuid();
        var responderDeviceId = Guid.NewGuid();
        var originDeviceId = Guid.NewGuid();

        new AuditIngestStore().Append(originDeviceId, MakeChain(originDeviceId, 5));

        var service = new AuditSyncService(() => MakeIdentity(customerGroupId, responderDeviceId), new AuditLog(() => responderDeviceId));
        service.StartListening(pushPort, meshPort);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, meshPort);
            await using var stream = client.GetStream();

            var request = new AuditDigestRequestMessage(customerGroupId, Guid.NewGuid(), new Dictionary<Guid, long> { [originDeviceId] = 2 });
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, WireOptions) + "\n");
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();

            using var readCts = new CancellationTokenSource(TestTimeout);
            var line = await BoundedLineReader.ReadLineAsync(stream, readCts.Token);
            Assert.NotNull(line);
            var response = JsonSerializer.Deserialize<AuditDigestResponseMessage>(line!, WireOptions);

            Assert.NotNull(response);
            Assert.Equal(5, response!.ResponderHighWaterMarks[originDeviceId]);
            Assert.Equal(new long[] { 3, 4, 5 }, response.EntriesForRequester.Select(e => e.Seq)); // Requester hatte nur bis Seq 2
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuditSyncService_ReconcileWithAdminPeerAsync_IngestsPeerEntries_AndPushesBackWhereAhead()
    {
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        var meshPort = GetFreeTcpPort();
        var pushBackPort = GetFreeTcpPort();
        var customerGroupId = Guid.NewGuid();
        var ownDeviceId = Guid.NewGuid();

        var aheadOriginDeviceId = Guid.NewGuid(); // wo WIR den Peer voraus sind
        var newFromPeerOriginDeviceId = Guid.NewGuid(); // Gerät, das wir noch gar nicht kennen, Peer aber schon

        var ingest = new AuditIngestStore();
        ingest.Append(aheadOriginDeviceId, MakeChain(aheadOriginDeviceId, 3));

        using var meshListener = new TcpListener(IPAddress.Loopback, meshPort);
        meshListener.Start();
        var meshTask = Task.Run(async () =>
        {
            using var client = await meshListener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var readCts = new CancellationTokenSource(TestTimeout);
            await BoundedLineReader.ReadLineAsync(stream, readCts.Token); // Digest-Request, Inhalt hier nicht relevant

            var response = new AuditDigestResponseMessage(
                customerGroupId,
                new Dictionary<Guid, long> { [aheadOriginDeviceId] = 0 }, // Peer kennt unser Ursprungsgerät noch gar nicht
                MakeChain(newFromPeerOriginDeviceId, 2));
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, WireOptions) + "\n");
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
        });

        var pushBackConnections = 0;
        using var pushBackListener = new TcpListener(IPAddress.Loopback, pushBackPort);
        pushBackListener.Start();
        var pushBackTask = Task.Run(async () =>
        {
            using var client = await pushBackListener.AcceptTcpClientAsync();
            Interlocked.Increment(ref pushBackConnections);
        });

        var service = new AuditSyncService(() => MakeIdentity(customerGroupId, ownDeviceId), new AuditLog(() => ownDeviceId));
        var peer = new DeviceEntry { DeviceId = Guid.NewGuid(), Role = Role.Admin, IpAddress = "127.0.0.1" };

        await service.ReconcileWithAdminPeerAsync(peer, meshPort: meshPort, pushBackPort: pushBackPort);
        await Task.WhenAll(meshTask, pushBackTask).WaitAsync(TestTimeout);

        var marks = ingest.GetHighWaterMarks();
        Assert.Equal(3, marks[aheadOriginDeviceId]); // unverändert, unser eigener Stand
        Assert.Equal(2, marks[newFromPeerOriginDeviceId]); // neu aus der Digest-Antwort übernommen
        Assert.Equal(1, pushBackConnections); // Gegenrichtung wurde angestoßen, weil wir bei aheadOriginDeviceId voraus waren
    }

    [Fact]
    public async Task AuditSyncService_PushPendingAsync_FullRoundTrip_ThroughSecureEnvelope_WhenAdminPeerIsCapable()
    {
        // LAN-Verschlüsselung (CLAUDE.md "Lizenz & Secrets"): gleicher Testaufbau wie
        // AuditSyncService_PushPendingAsync_FullRoundTrip_IngestsAndUpdatesPerAdminState,
        // nur mit Gruppenschlüssel + als verschlüsselungsfähig+gepinnt markiertem
        // Admin-Peer - beide Dienste laufen im selben Testprozess und teilen sich daher
        // denselben (prozessweit gecachten) DeviceIdentityStore-Schlüssel; das reicht, um
        // den Sende-/Empfangs-/Dispatch-Pfad über SecureEnvelope zu beweisen (die
        // eigentliche Kryptologik mit zwei UNTERSCHIEDLICHEN Geräte-Schlüsseln ist bereits
        // in SecureEnvelopeTests/AlarmChannelEncryptionTests abgedeckt).
        using var appData = new TestAppDataScope();
        using var sharedLog = SharedLogPaths.ForceLocalFallbackForTests();
        DeviceIdentityStore.ResetCacheForTests();
        var pushPort = GetFreeTcpPort();
        var meshPort = GetFreeTcpPort();
        var customerGroupId = Guid.NewGuid();
        var adminDeviceId = Guid.NewGuid();
        var groupKeyBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var sharedPublicKey = DeviceIdentityStore.LoadOrCreate().PublicKeyBase64;
        var senderDeviceId = Guid.NewGuid();

        // Simuliert vorherigen direkten Boot-Call-Kontakt (TOFU-Pinning, siehe
        // DiscoveryService) - ohne diesen Eintrag könnte die Admin-Empfangsseite die
        // Signatur des Senders nicht verifizieren (kein gepinnter Schlüssel bekannt).
        new DeviceStore().Save(new List<DeviceEntry>
        {
            new() { DeviceId = senderDeviceId, PinnedDeviceIdentityPublicKeyBase64 = sharedPublicKey },
        });

        var adminService = new AuditSyncService(() => MakeIdentity(customerGroupId, adminDeviceId), new AuditLog(() => adminDeviceId), groupKeyProvider: () => groupKeyBase64);
        adminService.StartListening(pushPort, meshPort);

        try
        {
            var senderLog = new AuditLog(() => senderDeviceId);
            senderLog.Append("alarm gesendet");
            senderLog.Append("alarm beendet");
            var senderService = new AuditSyncService(() => MakeIdentity(customerGroupId, senderDeviceId), senderLog, groupKeyProvider: () => groupKeyBase64);

            var adminPeer = new DeviceEntry
            {
                DeviceId = adminDeviceId,
                Role = Role.Admin,
                IpAddress = "127.0.0.1",
                TcpPort = pushPort,
                ProtocolVersion = AppConstants.CurrentProtocolVersion,
                PinnedDeviceIdentityPublicKeyBase64 = sharedPublicKey,
            };

            await senderService.PushPendingAsync(new[] { adminPeer }, pushPort: pushPort);

            Assert.Equal(2, new AuditIngestStore().GetHighWaterMarks()[senderDeviceId]);
            var stateAfterPush = new AuditSyncStateStore().Load();
            Assert.Equal(2, stateAfterPush.LastAckedSeqByAdmin[adminDeviceId]); // Ack kam korrekt entschlüsselt zurück

            await senderService.DisposeAsync();
        }
        finally
        {
            await adminService.DisposeAsync();
        }
    }
}
