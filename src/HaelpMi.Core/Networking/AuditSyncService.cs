using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Security;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Push-Sync für das revisionssichere Audit-Log (Nutzerwunsch 14./15.08.2026): jedes
/// Gerät pusht bei ohnehin stattfindenden Ereignissen (eigener Boot-Call, Alarm-Ende) die
/// seit dem letzten Mal an einen bestimmten Admin noch nicht bestätigten eigenen
/// AuditLog-Einträge an alle gerade erreichbaren Admin-Geräte - kein Heartbeat, kein
/// Polling, nur Huckepack auf bestehende Trigger, analog zum Update-Rollout ("gibt es
/// beim eigenen nächsten Boot-Call an Peers weiter").
///
/// Tamper-Evidence kommt NICHT aus einer Signatur (CLAUDE.md: kein viertes
/// Schlüsselpaar ohne konkreten, abgestimmten Bedarf), sondern aus der Kombination
/// Hash-Chain (siehe AuditLog/AuditLogEntry) + externer Zeugenschaft: sobald mindestens
/// ein Admin eine Kopie hat (siehe AuditIngestStore), kann das Ursprungsgerät seine
/// Historie nicht mehr unbemerkt umschreiben.
///
/// Admin-Rollen-Kryptoverifikation (Nutzerwunsch 17.08.2026, viertes Schlüsselpaar - siehe
/// CLAUDE.md "Lizenz &amp; Secrets"): <see cref="DeviceEntry.Role"/> allein war früher eine
/// unauthentifizierte Selbstauskunft des Peers - <see cref="PushPendingAsync"/> und
/// <see cref="HandleIncomingDigestAsync"/> vertrauen seitdem zusätzlich
/// <see cref="DeviceEntry.AdminVerified"/> (nur von DiscoveryService bei direktem,
/// signaturgeprüftem Boot-Call-Kontakt gesetzt, siehe Security.AdminRoleVerifier).
///
/// Empfangsseite (<see cref="StartListening"/>) darf nur gestartet werden, wenn das eigene
/// Gerät Role.Admin ist - Gating liegt beim Aufrufer (HaelpMi.Agent/App.xaml.cs), nicht hier.
///
/// Admin&lt;-&gt;Admin-Mesh-Abgleich (<see cref="ReconcileWithAdminPeerAsync"/>): sobald
/// zwei Admin-Geräte sich per Boot-Call begegnen (siehe DiscoveryService.
/// AdminPeerContactObserved), tauschen sie einen kleinen Digest (höchste bekannte Seq pro
/// Ursprungsgerät) und füllen sich in beide Richtungen gegenseitig die Lücken auf - der
/// Stand unter mehreren gleichzeitig erreichbaren Admins konvergiert damit so schnell wie
/// möglich, macht Daten aber nicht frischer, als was ein Ursprungsgerät überhaupt schon
/// mal irgendeinem Admin gepusht hat.
/// </summary>
public sealed class AuditSyncService : IAsyncDisposable
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly AuditLog _auditLog;
    private readonly AuditIngestStore _ingestStore = new();
    private readonly AuditSyncStateStore _stateStore = new();
    private readonly Action<string>? _audit;
    private readonly Func<string?>? _groupKeyProvider;
    private readonly DeviceStore _deviceStore = new();

    private TcpListener? _pushListener;
    private TcpListener? _meshListener;
    private CancellationTokenSource? _cts;
    private Task? _pushAcceptLoop;
    private Task? _meshAcceptLoop;

    /// <param name="groupKeyProvider">Siehe AlarmSender-Konstruktor - gleiche Bedeutung, LAN-Verschlüsselung von Push und Mesh-Digest.</param>
    public AuditSyncService(Func<LiveIdentity> identityProvider, AuditLog auditLog, Action<string>? audit = null, Func<string?>? groupKeyProvider = null)
    {
        _identityProvider = identityProvider;
        _auditLog = auditLog;
        _audit = audit;
        _groupKeyProvider = groupKeyProvider;
    }

    /// <summary>Nur aufrufen, wenn das eigene Gerät Role.Admin ist (siehe Klassendoku) - Push-Sendeseite läuft unabhängig davon auf jedem Gerät.</summary>
    public void StartListening(int? pushPort = null, int? meshPort = null)
    {
        if (_pushListener is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();

        // Zwei unabhängige, jeweils gracefully fehlschlagende Listener (gleiches
        // Fallback-Muster wie EditLockService/ConfigSyncService.Start()) - ein belegter
        // Port für den einen darf den anderen nicht verhindern.
        try
        {
            _pushListener = new TcpListener(IPAddress.Any, pushPort ?? AppConstants.AuditSyncTcpPort);
            _pushListener.Start();
            _pushAcceptLoop = AcceptLoopAsync(_pushListener, HandleIncomingPushAsync, _cts.Token);
        }
        catch (SocketException)
        {
            _pushListener = null;
        }

        try
        {
            _meshListener = new TcpListener(IPAddress.Any, meshPort ?? AppConstants.AuditMeshTcpPort);
            _meshListener.Start();
            _meshAcceptLoop = AcceptLoopAsync(_meshListener, HandleIncomingDigestAsync, _cts.Token);
        }
        catch (SocketException)
        {
            _meshListener = null;
        }
    }

    // ---------------------------------------------------------------------
    // Sendeseite: eigenes AuditLog -> erreichbare Admin-Geräte
    // ---------------------------------------------------------------------

    /// <param name="pushPort">Nur für Tests gedacht (gleiches Muster wie ConfigSyncService.Start(tcpPort)) - Produktivpfad nutzt immer AppConstants.AuditSyncTcpPort.</param>
    /// <summary>Bei eigenem Boot-Call bzw. Alarm-Ende aufrufen (siehe Aufrufer in HaelpMi.Agent) - best-effort, wirft nie.</summary>
    public async Task PushPendingAsync(IReadOnlyList<DeviceEntry> devices, CancellationToken ct = default, int? pushPort = null)
    {
        var identity = _identityProvider();
        var adminPeers = devices.Where(d => d.Role == Role.Admin && d.AdminVerified && d.DeviceId != identity.DeviceId).ToList();
        if (adminPeers.Count == 0)
        {
            return;
        }

        var resolvedPort = pushPort ?? AppConstants.AuditSyncTcpPort;
        var state = _stateStore.Load();
        await Task.WhenAll(adminPeers.Select(peer => PushToOneAsync(peer, identity, state, resolvedPort, ct)));
        _stateStore.Save(state);
    }

    private async Task PushToOneAsync(DeviceEntry peer, LiveIdentity identity, AuditSyncState state, int port, CancellationToken ct)
    {
        state.LastAckedSeqByAdmin.TryGetValue(peer.DeviceId, out var lastAcked);
        var entries = _auditLog.ReadSince(lastAcked, AppConstants.AuditSyncBatchCap);
        if (entries.Count == 0)
        {
            return;
        }

        var request = new AuditPushMessage(identity.CustomerGroupId, identity.DeviceId, entries, DateTimeOffset.UtcNow);
        var ack = await SendPushAsync(peer, port, request, ct);
        if (ack is { Accepted: true })
        {
            state.LastAckedSeqByAdmin[peer.DeviceId] = ack.AcceptedUpToSeq;
            _audit?.Invoke($"auditsync pushed entries={entries.Count} to admin={peer.DeviceId} acceptedUpToSeq={ack.AcceptedUpToSeq}");
        }
        // Kein Ack (unerreichbar/Timeout): state bleibt unverändert, nächster Trigger versucht es erneut.
    }

    private async Task<AuditPushAckMessage?> SendPushAsync(DeviceEntry peer, int port, AuditPushMessage request, CancellationToken ct)
    {
        try
        {
            if (!IPAddress.TryParse(peer.IpAddress, out var address))
            {
                return null;
            }

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(AppConstants.AuditSyncRequestTimeout);

            await client.ConnectAsync(address, port, timeoutCts.Token);
            await using var stream = client.GetStream();

            // LAN-Verschlüsselung (CLAUDE.md "Lizenz & Secrets"): gleiche Fallback-Logik
            // wie AlarmSender/ConfigSyncService - nur wenn wir einen Gruppenschlüssel haben
            // UND der Peer als verschlüsselungsfähig+gepinnt bekannt ist.
            var groupKeyBase64 = _groupKeyProvider?.Invoke();
            var canEncrypt = groupKeyBase64 is not null
                && peer.ProtocolVersion is >= AppConstants.CurrentProtocolVersion
                && !string.IsNullOrEmpty(peer.PinnedDeviceIdentityPublicKeyBase64);

            string requestLine;
            if (canEncrypt)
            {
                var devicePrivateKeyBase64 = DeviceIdentityStore.LoadOrCreate().PrivateKeyBase64;
                var envelope = SecureEnvelopeCodec.Seal(request, request.CustomerGroupId, request.SenderDeviceId, groupKeyBase64, devicePrivateKeyBase64, DateTimeOffset.UtcNow);
                requestLine = envelope is not null ? NetworkSerializer.ToJsonLine(envelope) : NetworkSerializer.ToJsonLine(request);
            }
            else
            {
                requestLine = NetworkSerializer.ToJsonLine(request);
            }

            var payload = NetworkSerializer.Encoding.GetBytes(requestLine);
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return null;
            }

            if (SecureEnvelopeCodec.TryParse(line, out var ackEnvelope) && ackEnvelope is not null)
            {
                return SecureEnvelopeCodec.TryOpen<AuditPushAckMessage>(ackEnvelope, groupKeyBase64, peer.PinnedDeviceIdentityPublicKeyBase64, DateTimeOffset.UtcNow);
            }

            return NetworkSerializer.FromJsonLine<AuditPushAckMessage>(line);
        }
        catch (Exception)
        {
            return null; // unerreichbar - bleibt "pending", nächster Trigger versucht es erneut
        }
    }

    // ---------------------------------------------------------------------
    // Empfangsseite: Push annehmen (nur wenn Role.Admin, siehe StartListening)
    // ---------------------------------------------------------------------

    private async Task HandleIncomingPushAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 10000;
            client.SendTimeout = 5000;
            await using var stream = client.GetStream();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            var identity = _identityProvider();

            AuditPushMessage? request;
            var wasEncrypted = false;

            if (SecureEnvelopeCodec.TryParse(line, out var envelope) && envelope is not null)
            {
                if (!CustomerGroupFilter.Matches(envelope.CustomerGroupId, identity.CustomerGroupId) || !envelope.IsPlausible())
                {
                    return;
                }

                var senderPinnedKey = _deviceStore.Load().FirstOrDefault(d => d.DeviceId == envelope.DeviceId)?.PinnedDeviceIdentityPublicKeyBase64;
                var groupKeyBase64 = _groupKeyProvider?.Invoke();
                request = SecureEnvelopeCodec.TryOpen<AuditPushMessage>(envelope, groupKeyBase64, senderPinnedKey, DateTimeOffset.UtcNow);
                if (request is null)
                {
                    return; // falscher Gruppenschlüssel/manipuliert/falscher Absender-Schlüssel - stiller Drop
                }

                wasEncrypted = true;
            }
            else
            {
                try
                {
                    request = NetworkSerializer.FromJsonLine<AuditPushMessage>(line);
                }
                catch (Exception)
                {
                    return;
                }

                if (request is null || !CustomerGroupFilter.Matches(request.CustomerGroupId, identity.CustomerGroupId))
                {
                    return; // Teil 2, Abschnitt 6
                }
            }

            if (request.SenderDeviceId == Guid.Empty || request.Entries.Count == 0)
            {
                return;
            }

            // Nach Ursprungsgerät gruppieren - ein Push (direkt vom Ursprungsgerät ODER von
            // einem weiterleitenden Admin beim Mesh-Abgleich) kann Einträge mehrerer
            // Ursprungsgeräte enthalten (siehe AuditPushMessage-Klassendoku).
            long acceptedUpToSeq = 0;
            var gapDetected = false;
            foreach (var group in request.Entries.GroupBy(e => e.OriginDeviceId))
            {
                var result = _ingestStore.Append(group.Key, group.OrderBy(e => e.Seq).ToList());
                gapDetected |= result.GapDetected;
                if (group.Key == request.SenderDeviceId)
                {
                    acceptedUpToSeq = group.Max(e => e.Seq);
                }
            }

            _audit?.Invoke($"auditsync received from device={request.SenderDeviceId} entries={request.Entries.Count} gap={gapDetected}");

            var response = new AuditPushAckMessage(identity.CustomerGroupId, true, acceptedUpToSeq, gapDetected);

            string responseLine;
            if (wasEncrypted)
            {
                var groupKeyBase64 = _groupKeyProvider?.Invoke();
                var devicePrivateKeyBase64 = DeviceIdentityStore.LoadOrCreate().PrivateKeyBase64;
                var responseEnvelope = SecureEnvelopeCodec.Seal(response, response.CustomerGroupId, identity.DeviceId, groupKeyBase64, devicePrivateKeyBase64, DateTimeOffset.UtcNow);
                responseLine = responseEnvelope is not null ? NetworkSerializer.ToJsonLine(responseEnvelope) : NetworkSerializer.ToJsonLine(response);
            }
            else
            {
                responseLine = NetworkSerializer.ToJsonLine(response);
            }

            var responseBytes = NetworkSerializer.Encoding.GetBytes(responseLine);
            await stream.WriteAsync(responseBytes, ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception)
        {
            // best-effort - der Absender behandelt eine fehlgeschlagene Antwort wie einen
            // unerreichbaren Peer und versucht es beim nächsten eigenen Trigger erneut.
        }
    }

    // ---------------------------------------------------------------------
    // Admin<->Admin Mesh-Abgleich
    // ---------------------------------------------------------------------

    /// <summary>An DiscoveryService.AdminPeerContactObserved hängen (nur sinnvoll, wenn das eigene Gerät selbst Role.Admin ist - das Event feuert dort ohnehin nur dann).</summary>
    public void OnAdminPeerContactObserved(object? sender, AdminPeerContactInfo info) =>
        _ = ReconcileWithAdminPeerAsync(info.Peer, CancellationToken.None);

    /// <param name="meshPort">Nur für Tests gedacht.</param>
    /// <param name="pushBackPort">Nur für Tests gedacht - Port für die Gegenrichtung (siehe SendPushAsync-Aufruf unten).</param>
    public async Task ReconcileWithAdminPeerAsync(DeviceEntry adminPeer, CancellationToken ct = default, int? meshPort = null, int? pushBackPort = null)
    {
        try
        {
            if (!IPAddress.TryParse(adminPeer.IpAddress, out var address))
            {
                return;
            }

            var identity = _identityProvider();
            var myMarks = _ingestStore.GetHighWaterMarks();
            var request = new AuditDigestRequestMessage(identity.CustomerGroupId, identity.DeviceId, myMarks);

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(AppConstants.AuditSyncRequestTimeout);

            await client.ConnectAsync(address, meshPort ?? AppConstants.AuditMeshTcpPort, timeoutCts.Token);
            await using var stream = client.GetStream();

            // LAN-Verschlüsselung (CLAUDE.md "Lizenz & Secrets"): gleiche Fallback-Logik wie
            // überall sonst in diesem Kanal.
            var groupKeyBase64 = _groupKeyProvider?.Invoke();
            var canEncrypt = groupKeyBase64 is not null
                && adminPeer.ProtocolVersion is >= AppConstants.CurrentProtocolVersion
                && !string.IsNullOrEmpty(adminPeer.PinnedDeviceIdentityPublicKeyBase64);

            string requestLine;
            if (canEncrypt)
            {
                var devicePrivateKeyBase64 = DeviceIdentityStore.LoadOrCreate().PrivateKeyBase64;
                var requestEnvelope = SecureEnvelopeCodec.Seal(request, request.CustomerGroupId, request.RequesterDeviceId, groupKeyBase64, devicePrivateKeyBase64, DateTimeOffset.UtcNow);
                requestLine = requestEnvelope is not null ? NetworkSerializer.ToJsonLine(requestEnvelope) : NetworkSerializer.ToJsonLine(request);
            }
            else
            {
                requestLine = NetworkSerializer.ToJsonLine(request);
            }

            var payload = NetworkSerializer.Encoding.GetBytes(requestLine);
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            AuditDigestResponseMessage? response;
            if (SecureEnvelopeCodec.TryParse(line, out var responseEnvelope) && responseEnvelope is not null)
            {
                response = SecureEnvelopeCodec.TryOpen<AuditDigestResponseMessage>(responseEnvelope, groupKeyBase64, adminPeer.PinnedDeviceIdentityPublicKeyBase64, DateTimeOffset.UtcNow);
            }
            else
            {
                response = NetworkSerializer.FromJsonLine<AuditDigestResponseMessage>(line);
            }

            if (response is null)
            {
                return;
            }

            if (response.EntriesForRequester.Count > 0)
            {
                foreach (var group in response.EntriesForRequester.GroupBy(e => e.OriginDeviceId))
                {
                    _ingestStore.Append(group.Key, group.OrderBy(e => e.Seq).ToList());
                }

                _audit?.Invoke($"auditsync mesh pulled entries={response.EntriesForRequester.Count} from admin={adminPeer.DeviceId}");
            }

            // Umgekehrte Richtung: wo WIR weiter sind als der Peer, direkt zurückpushen -
            // derselbe Push-Kanal wie beim normalen Origin->Admin-Push, nur hier Admin->Admin
            // (siehe AuditPushMessage-Klassendoku zu SenderDeviceId vs. OriginDeviceId).
            var toPush = new List<AuditLogEntry>();
            foreach (var (originId, myLastSeq) in myMarks)
            {
                response.ResponderHighWaterMarks.TryGetValue(originId, out var peerLastSeq);
                if (myLastSeq > peerLastSeq)
                {
                    toPush.AddRange(_ingestStore.ReadSince(originId, peerLastSeq, AppConstants.AuditSyncBatchCap));
                }
            }

            if (toPush.Count > 0)
            {
                var pushRequest = new AuditPushMessage(identity.CustomerGroupId, identity.DeviceId, toPush, DateTimeOffset.UtcNow);
                await SendPushAsync(adminPeer, pushBackPort ?? AppConstants.AuditSyncTcpPort, pushRequest, ct);
                _audit?.Invoke($"auditsync mesh pushed entries={toPush.Count} to admin={adminPeer.DeviceId}");
            }
        }
        catch (Exception)
        {
            // best-effort - der nächste Boot-Kontakt zwischen denselben zwei Admins versucht es erneut
        }
    }

    private async Task HandleIncomingDigestAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 10000;
            client.SendTimeout = 10000;
            await using var stream = client.GetStream();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            var identity = _identityProvider();

            AuditDigestRequestMessage? request;
            var wasEncrypted = false;

            if (SecureEnvelopeCodec.TryParse(line, out var envelope) && envelope is not null)
            {
                if (!CustomerGroupFilter.Matches(envelope.CustomerGroupId, identity.CustomerGroupId) || !envelope.IsPlausible())
                {
                    return;
                }

                var requesterPinnedKey = _deviceStore.Load().FirstOrDefault(d => d.DeviceId == envelope.DeviceId)?.PinnedDeviceIdentityPublicKeyBase64;
                var groupKeyBase64 = _groupKeyProvider?.Invoke();
                request = SecureEnvelopeCodec.TryOpen<AuditDigestRequestMessage>(envelope, groupKeyBase64, requesterPinnedKey, DateTimeOffset.UtcNow);
                if (request is null)
                {
                    return; // falscher Gruppenschlüssel/manipuliert/falscher Absender-Schlüssel - stiller Drop
                }

                wasEncrypted = true;
            }
            else
            {
                try
                {
                    request = NetworkSerializer.FromJsonLine<AuditDigestRequestMessage>(line);
                }
                catch (Exception)
                {
                    return;
                }

                if (request is null || !CustomerGroupFilter.Matches(request.CustomerGroupId, identity.CustomerGroupId))
                {
                    return;
                }
            }

            if (request.RequesterDeviceId == Guid.Empty)
            {
                return;
            }

            // Admin-Rollen-Kryptoverifikation (Nutzerwunsch 17.08.2026): ohne diese Prüfung
            // würde JEDES Gerät, das sich die Mühe macht, direkt auf diesen Port zu
            // verbinden, die gesamten gesammelten Audit-Daten abgreifen können - eine
            // Vertraulichkeitslücke, die über die reine Koordinationsfrage hinausgeht.
            // Wiederverwendet dieselbe AdminVerified-Grundlage wie PushPendingAsync, keine
            // eigene Signaturprüfung auf dieser Nachricht nötig.
            var requesterIsVerifiedAdmin = _deviceStore.Load()
                .Any(d => d.DeviceId == request.RequesterDeviceId && d.Role == Role.Admin && d.AdminVerified);
            if (!requesterIsVerifiedAdmin)
            {
                return;
            }

            var myMarks = _ingestStore.GetHighWaterMarks();
            var entriesForRequester = new List<AuditLogEntry>();
            foreach (var (originId, myLastSeq) in myMarks)
            {
                request.RequesterHighWaterMarks.TryGetValue(originId, out var requesterLastSeq);
                if (myLastSeq > requesterLastSeq)
                {
                    entriesForRequester.AddRange(_ingestStore.ReadSince(originId, requesterLastSeq, AppConstants.AuditSyncBatchCap));
                }
            }

            var response = new AuditDigestResponseMessage(identity.CustomerGroupId, myMarks, entriesForRequester);

            string responseLine;
            if (wasEncrypted)
            {
                var groupKeyBase64 = _groupKeyProvider?.Invoke();
                var devicePrivateKeyBase64 = DeviceIdentityStore.LoadOrCreate().PrivateKeyBase64;
                var responseEnvelope = SecureEnvelopeCodec.Seal(response, response.CustomerGroupId, identity.DeviceId, groupKeyBase64, devicePrivateKeyBase64, DateTimeOffset.UtcNow);
                responseLine = responseEnvelope is not null ? NetworkSerializer.ToJsonLine(responseEnvelope) : NetworkSerializer.ToJsonLine(response);
            }
            else
            {
                responseLine = NetworkSerializer.ToJsonLine(response);
            }

            var responseBytes = NetworkSerializer.Encoding.GetBytes(responseLine);
            await stream.WriteAsync(responseBytes, ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception)
        {
            // best-effort - der Requester behandelt eine fehlgeschlagene Antwort wie einen
            // unerreichbaren Peer und versucht es beim nächsten Boot-Kontakt erneut.
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, Func<TcpClient, CancellationToken, Task> handler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            _ = handler(client, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _pushListener?.Stop();
        _meshListener?.Stop();
        if (_pushAcceptLoop is not null)
        {
            try { await _pushAcceptLoop; } catch { /* handled inside loop */ }
        }
        if (_meshAcceptLoop is not null)
        {
            try { await _meshAcceptLoop; } catch { /* handled inside loop */ }
        }
        _cts?.Dispose();
    }
}
