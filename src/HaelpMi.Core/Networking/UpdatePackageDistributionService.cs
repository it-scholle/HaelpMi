using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Storage;
using HaelpMi.Core.Updates;

namespace HaelpMi.Core.Networking;

/// <summary>
/// DEPRECATED für release-1.0-MVP: wird zur Laufzeit nirgends gestartet (siehe
/// HaelpMi.Agent/App.xaml.cs) - keine P2P-Update-Verteilung auf diesem Release-Zweig.
///
/// P2P-Verteilung eines signierten Update-Pakets (Abschnitt 11), gleiches Muster wie
/// <see cref="ConfigSyncService"/>s Pull-Kanal: die Erkennung "es gibt eine neuere
/// Version" läuft bereits über den bestehenden Boot-Call
/// (<see cref="DiscoveryService.PeerVersionObserved"/>), hier geht es nur noch um den
/// eigentlichen Bezug des Pakets von einem konkreten Peer. Jedes Gerät serviert dabei nur
/// Versionen, die es selbst schon in seinem <see cref="UpdatePackageCacheStore"/> hat -
/// wie das allererste Gerät im Netz zu einer neuen Version kommt (z. B. durch den Admin
/// manuell verteilt) ist bewusst kein Teil dieser Klasse, sondern eine Betriebsfrage
/// außerhalb der P2P-Pipeline.
/// </summary>
public sealed class UpdatePackageDistributionService : IAsyncDisposable
{
    // Großzügig für ein self-contained .NET-Publish-Output, aber ein hartes Limit gegen
    // eine böswillige/fehlerhafte Gegenstelle, die eine absurde Länge behauptet und uns
    // damit zu einer riesigen Speicherzuteilung verleiten will (CLAUDE.md: kein
    // Vertrauen in Netzwerk-Eingaben).
    private const long MaxPayloadBytes = 300L * 1024 * 1024;

    private readonly Func<LiveIdentity> _identityProvider;
    private readonly UpdatePackageCacheStore _cacheStore;
    private readonly Action<string>? _audit;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public UpdatePackageDistributionService(Func<LiveIdentity> identityProvider, UpdatePackageCacheStore cacheStore, Action<string>? audit = null)
    {
        _identityProvider = identityProvider;
        _cacheStore = cacheStore;
        _audit = audit;
    }

    public void Start(int port = AppConstants.UpdatePackageTcpPort)
    {
        if (_listener is not null)
        {
            return;
        }

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(_listener, _cts.Token);
    }

    /// <summary>Requester-Seite: ein bestimmtes Paket von einem konkreten, bereits bekannten Peer beziehen.</summary>
    public async Task<(UpdatePackageManifest Manifest, byte[] Payload)?> PullFromAsync(DeviceEntry peer, string version, CancellationToken ct = default)
    {
        try
        {
            if (!IPAddress.TryParse(peer.IpAddress, out var address))
            {
                return null;
            }

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60)); // größere Payload als die übrigen, sehr kurzen P2P-Calls

            await client.ConnectAsync(address, AppConstants.UpdatePackageTcpPort, timeoutCts.Token);
            await using var stream = client.GetStream();

            var identity = _identityProvider();
            var request = new UpdatePullRequestMessage(identity.CustomerGroupId, identity.DeviceId, version);
            var requestBytes = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(request));
            await stream.WriteAsync(requestBytes, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            var headerLine = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (headerLine is null)
            {
                return null;
            }

            var header = NetworkSerializer.FromJsonLine<UpdatePullResponseHeader>(headerLine);
            if (header is null || !header.Found || header.Manifest is null)
            {
                return null;
            }

            if (header.PayloadLength <= 0 || header.PayloadLength > MaxPayloadBytes)
            {
                return null;
            }

            var payload = new byte[header.PayloadLength];
            var offset = 0;
            while (offset < payload.Length)
            {
                var read = await stream.ReadAsync(payload.AsMemory(offset), timeoutCts.Token);
                if (read == 0)
                {
                    return null; // Verbindung mitten in der Übertragung abgebrochen
                }
                offset += read;
            }

            if (!UpdatePackageVerifier.Verify(payload, header.Manifest))
            {
                _audit?.Invoke($"update package signature verification failed version={version} peer={peer.DeviceId}");
                return null;
            }

            return (header.Manifest, payload);
        }
        catch (Exception)
        {
            return null;
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

            _ = HandleConnectionAsync(client, ct);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 60000;
            await using var stream = client.GetStream();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            var request = NetworkSerializer.FromJsonLine<UpdatePullRequestMessage>(line);
            if (request is null || request.RequesterDeviceId == Guid.Empty || string.IsNullOrWhiteSpace(request.Version))
            {
                return;
            }

            var identity = _identityProvider();
            if (!CustomerGroupFilter.Matches(request.CustomerGroupId, identity.CustomerGroupId))
            {
                return;
            }

            var cached = _cacheStore.TryLoad(request.Version);
            var responseTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            responseTimeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

            if (cached is null)
            {
                var notFound = new UpdatePullResponseHeader(identity.CustomerGroupId, false, null, 0);
                await WriteHeaderAsync(stream, notFound, responseTimeoutCts.Token);
                return;
            }

            var (manifest, payload) = cached.Value;
            var header = new UpdatePullResponseHeader(identity.CustomerGroupId, true, manifest, payload.LongLength);
            await WriteHeaderAsync(stream, header, responseTimeoutCts.Token);
            await stream.WriteAsync(payload, responseTimeoutCts.Token);
            await stream.FlushAsync(responseTimeoutCts.Token);

            _audit?.Invoke($"update package served version={request.Version} to deviceId={request.RequesterDeviceId}");
        }
        catch (Exception)
        {
            // best-effort - der Requester wertet eine fehlgeschlagene/leere Antwort einfach als "Peer hat nichts" und probiert einen anderen
        }
    }

    private static async Task WriteHeaderAsync(NetworkStream stream, UpdatePullResponseHeader header, CancellationToken ct)
    {
        var bytes = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(header));
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
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
