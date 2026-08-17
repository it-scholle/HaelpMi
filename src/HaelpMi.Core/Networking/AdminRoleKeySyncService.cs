using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Security;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Migrationspfad-Schlüsselaustausch für die Admin-Rollen-Signatur (Nutzerwunsch
/// 17.08.2026, siehe Security.AdminRoleTrustStore-Klassendoku): läuft ausschließlich auf
/// Admin-Geräten (Sende- wie Empfangsseite, gleiches Rollen-Gating wie AuditSyncService),
/// hängt am ohnehin stattfindenden Boot-Call-Kontakt (<see
/// cref="DiscoveryService.AdminPeerContactObserved"/>) - kein eigener Discovery-Kanal.
///
/// Ablauf: JEDES Admin-Gerät fragt bei JEDEM direkt kontaktierten Admin-Peer an (der
/// eigene Zustand wird mitgeschickt) - der Peer entscheidet daran, ob er seinen Schlüssel
/// zurückgibt (der Anfragende hat noch keinen, oder dessen ersetzbarer Schlüssel ist
/// lexikografisch größer als der eigene). Da der Boot-Call-Austausch symmetrisch ist
/// (Announce+Reply lösen das Event auf BEIDEN Seiten aus), braucht es keine zweite Runde
/// in eine Richtung - fragt der jeweils andere seinerseits an, bekommt er (falls
/// zutreffend) den kleineren Schlüssel zurück.
///
/// Der private Schlüssel verlässt ein Gerät NUR über einen erfolgreich geöffneten
/// SecureEnvelope (Gruppenschlüssel + geräte-signiert) - ohne diesen Kanal antwortet ein
/// Gerät mit leeren Feldern statt den Schlüssel unverschlüsselt preiszugeben (siehe
/// HandleIncomingRequestAsync). Ein Installer-Schlüssel (<see
/// cref="DeploymentInfo.AdminRolePrivateKeyBase64"/> gesetzt) wird nie ersetzt - das
/// eigene Gerät bietet ihn zwar an (er kann helfen, ein anderes Gerät zu bestücken), nimmt
/// aber selbst nie einen fremden an (<paramref name="isOwnKeyReplaceableProvider"/>).
/// </summary>
public sealed class AdminRoleKeySyncService : IAsyncDisposable
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Func<string?> _adminRolePrivateKeyProvider;
    private readonly Func<bool> _isOwnKeyReplaceableProvider;
    private readonly Func<string?>? _groupKeyProvider;
    private readonly Action<string>? _audit;
    private readonly DeviceStore _deviceStore = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    /// <param name="adminRolePrivateKeyProvider">Gleicher effektiver Schlüssel wie bei DiscoveryService (Installer ?? Trust-Store) - der öffentliche Teil steht in identityProvider().AdminRolePublicKeyBase64 (siehe AdminRoleTrustStore: eigener Schlüssel und eigene Gruppenschlüssel-Vorstellung sind für ein Admin-Gerät stets identisch).</param>
    /// <param name="isOwnKeyReplaceableProvider">true, solange der eigene Schlüssel NICHT aus deployment.json (Installer) stammt - nur dann darf eine Konvergenz ihn ersetzen.</param>
    public AdminRoleKeySyncService(
        Func<LiveIdentity> identityProvider,
        Func<string?> adminRolePrivateKeyProvider,
        Func<bool> isOwnKeyReplaceableProvider,
        Func<string?>? groupKeyProvider = null,
        Action<string>? audit = null)
    {
        _identityProvider = identityProvider;
        _adminRolePrivateKeyProvider = adminRolePrivateKeyProvider;
        _isOwnKeyReplaceableProvider = isOwnKeyReplaceableProvider;
        _groupKeyProvider = groupKeyProvider;
        _audit = audit;
    }

    /// <summary>Nur aufrufen, wenn das eigene Gerät Role.Admin ist (siehe Klassendoku).</summary>
    public void Start(int? port = null)
    {
        if (_listener is not null)
        {
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, port ?? AppConstants.AdminRoleKeySyncTcpPort);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _acceptLoop = AcceptLoopAsync(_listener, _cts.Token);
        }
        catch (SocketException)
        {
            // Gleiches Graceful-Fallback-Muster wie EditLockService/ConfigSyncService.Start():
            // ein belegter Port darf den restlichen Programmstart nie verhindern - dieser
            // Migrationspfad greift dann erst beim nächsten Prozessstart bzw. bleibt für
            // dieses Gerät auf den Installer-Weg angewiesen.
            _listener = null;
        }
    }

    /// <summary>An DiscoveryService.AdminPeerContactObserved hängen (nur sinnvoll, wenn das eigene Gerät selbst Role.Admin ist - das Event feuert dort ohnehin nur dann).</summary>
    public void OnAdminPeerContactObserved(object? sender, AdminPeerContactInfo info) =>
        _ = SyncWithPeerAsync(info.Peer, CancellationToken.None);

    private async Task SyncWithPeerAsync(DeviceEntry peer, CancellationToken ct, int? port = null)
    {
        try
        {
            if (!IPAddress.TryParse(peer.IpAddress, out var address))
            {
                return;
            }

            var identity = _identityProvider();
            var ownPublicKey = identity.AdminRolePublicKeyBase64;
            var request = new AdminRoleKeySyncRequestMessage(identity.CustomerGroupId, identity.DeviceId, ownPublicKey, _isOwnKeyReplaceableProvider());

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(AppConstants.AuditSyncRequestTimeout);

            await client.ConnectAsync(address, port ?? AppConstants.AdminRoleKeySyncTcpPort, timeoutCts.Token);
            await using var stream = client.GetStream();

            var payload = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(request));
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            AdminRoleKeySyncResponseMessage? response;
            if (SecureEnvelopeCodec.TryParse(line, out var envelope) && envelope is not null)
            {
                var groupKeyBase64 = _groupKeyProvider?.Invoke();
                response = SecureEnvelopeCodec.TryOpen<AdminRoleKeySyncResponseMessage>(envelope, groupKeyBase64, peer.PinnedDeviceIdentityPublicKeyBase64, DateTimeOffset.UtcNow);
            }
            else
            {
                response = NetworkSerializer.FromJsonLine<AdminRoleKeySyncResponseMessage>(line);
            }

            if (response?.PublicKeyBase64 is null || response.PrivateKeyBase64 is null)
            {
                return; // Peer hatte nichts (Verbotenes) zu bieten - nichts zu tun
            }

            var shouldAdopt = string.IsNullOrEmpty(ownPublicKey)
                || (_isOwnKeyReplaceableProvider() && string.CompareOrdinal(response.PublicKeyBase64, ownPublicKey) < 0);
            if (shouldAdopt)
            {
                AdminRoleTrustStore.SaveOwnKey(response.PublicKeyBase64, response.PrivateKeyBase64, AdminRoleTrustStore.PrivateKeyProvenance.PeerAdopted);
                _audit?.Invoke($"admin-role-key uebernommen von admin={peer.DeviceId}");
            }
        }
        catch (Exception)
        {
            // best-effort - der nächste Boot-Kontakt (mit demselben oder einem anderen
            // Admin-Peer) versucht es erneut, kein Zustellzwang wie beim Alarm-Kanal.
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
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

            _ = HandleIncomingRequestAsync(client, ct);
        }
    }

    private async Task HandleIncomingRequestAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            await using var stream = client.GetStream();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            AdminRoleKeySyncRequestMessage? request;
            try
            {
                request = NetworkSerializer.FromJsonLine<AdminRoleKeySyncRequestMessage>(line);
            }
            catch (Exception)
            {
                return;
            }

            if (request is null || request.RequesterDeviceId == Guid.Empty)
            {
                return;
            }

            var identity = _identityProvider();
            if (!CustomerGroupFilter.Matches(request.CustomerGroupId, identity.CustomerGroupId))
            {
                return; // Teil 2, Abschnitt 6
            }

            var ownPublicKey = identity.AdminRolePublicKeyBase64;
            var ownPrivateKey = _adminRolePrivateKeyProvider();

            var shouldOffer = !string.IsNullOrEmpty(ownPublicKey) && !string.IsNullOrEmpty(ownPrivateKey) &&
                (string.IsNullOrEmpty(request.OwnPublicKeyBase64)
                    || (request.IsReplaceable && string.CompareOrdinal(ownPublicKey, request.OwnPublicKeyBase64) < 0));

            // Der private Schlüssel verlässt dieses Gerät NUR verschlüsselt - fehlt der
            // Kanal (kein Gruppenschlüssel, oder Absender-Geräte-Identität noch nicht
            // gepinnt), bleibt die Antwort leer statt unverschlüsselt zu antworten
            // (Klassendoku).
            var senderPinnedDeviceKey = _deviceStore.Load().FirstOrDefault(d => d.DeviceId == request.RequesterDeviceId)?.PinnedDeviceIdentityPublicKeyBase64;
            var groupKeyBase64 = _groupKeyProvider?.Invoke();
            var canEncrypt = shouldOffer && groupKeyBase64 is not null && !string.IsNullOrEmpty(senderPinnedDeviceKey);

            var response = canEncrypt
                ? new AdminRoleKeySyncResponseMessage(identity.CustomerGroupId, ownPublicKey, ownPrivateKey)
                : new AdminRoleKeySyncResponseMessage(identity.CustomerGroupId, null, null);

            string responseLine;
            if (canEncrypt)
            {
                var devicePrivateKeyBase64 = DeviceIdentityStore.LoadOrCreate().PrivateKeyBase64;
                var responseEnvelope = SecureEnvelopeCodec.Seal(response, response.CustomerGroupId, identity.DeviceId, groupKeyBase64, devicePrivateKeyBase64, DateTimeOffset.UtcNow);
                responseLine = responseEnvelope is not null ? NetworkSerializer.ToJsonLine(responseEnvelope) : NetworkSerializer.ToJsonLine(new AdminRoleKeySyncResponseMessage(identity.CustomerGroupId, null, null));
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
            // best-effort - der Anfragende behandelt eine fehlgeschlagene Antwort wie einen
            // unerreichbaren Peer und versucht es beim nächsten Boot-Kontakt erneut.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* handled inside loop */ }
        }
        _cts?.Dispose();
    }
}
