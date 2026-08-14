using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Networking;

public sealed class PeerVersionInfo
{
    public required Guid DeviceId { get; init; }
    public required string ProgramVersion { get; init; }
    public required int ConfigVersion { get; init; }
}

/// <summary>Siehe <see cref="DiscoveryService.PeerConfigVersionObserved"/>.</summary>
public sealed class PeerConfigVersionInfo
{
    public required Guid DeviceId { get; init; }
    public required int ConfigVersion { get; init; }
}

/// <summary>
/// UDP boot-call discovery (Phase 1 5.5/FR-21/22/23, Teil 2 Abschnitt 9): one socket
/// bound to <see cref="AppConstants.DiscoveryUdpPort"/> both sends the once-per-startup
/// boot-call announce and continuously listens for other devices' announces/replies -
/// there is no heartbeat, so "continuously listening" simply means "for as long as the
/// Agent process runs", not any kind of polling.
///
/// Every message carries <see cref="LiveIdentity.CustomerGroupId"/>; anything with a
/// different id than ours is dropped in <see cref="HandleDatagramAsync"/> before it
/// touches the device list at all (Teil 2, Abschnitt 6 - customer/group isolation).
///
/// A plain limited broadcast (255.255.255.255) only reaches the default-route subnet,
/// matching the spec's accepted "same subnet/VLAN only" constraint. Multi-VLAN-Bridge-Seed
/// (Nutzerwunsch 13.08.2026): if <see cref="_bridgeSeedAddressProvider"/> resolves to a
/// non-empty address (typically an Admin-Gerät in a routed-but-not-broadcast-reachable
/// segment), <see cref="AnnounceAsync"/> additionally unicasts the same boot-call there -
/// the receiving <see cref="HandleDatagramAsync"/> doesn't distinguish unicast from
/// broadcast origin, so this "just works" as a bootstrap contact. Once that first contact
/// stands, the existing gossip (<c>KnownDeviceSummary</c> in a Reply) carries the rest of
/// the device list across the bridge on its own - the seed device itself needn't stay up
/// afterwards.
/// </summary>
public sealed class DiscoveryService : IAsyncDisposable
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly int _discoveryPort;
    private readonly DeviceStore _deviceStore = new();
    private readonly SemaphoreSlim _storeLock = new(1, 1);
    private readonly Action<string>? _audit;
    private readonly Func<string?>? _bridgeSeedAddressProvider;
    private UdpClient? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    public event EventHandler<DeviceEntry>? DeviceUpdated;

    /// <summary>Raised whenever a boot-call reveals a peer running a different program version than us - update-pipeline hook (Teil 2, Abschnitt 11).</summary>
    public event EventHandler<PeerVersionInfo>? PeerVersionObserved;

    /// <summary>
    /// Raised whenever a boot-call reveals a peer whose applied Config-Version is newer
    /// than ours - Config-Sync-Pull-Hook (Fehlerbericht 11.08.2026: drei frisch installierte
    /// Geräte blieben ohne Config, obwohl der Admin sie längst eingerichtet hatte, bis er
    /// eine weitere Änderung vorgenommen hat). Der dedizierte Config-Sync-Broadcast in
    /// ConfigSyncService.PublishAsync erreicht nur Geräte, die zum Zeitpunkt der jeweiligen
    /// Änderung schon liefen - ein danach gestartetes/frisch installiertes Gerät hat das
    /// damalige Announce nie gehört und bliebe sonst dauerhaft ohne Config, bis zufällig die
    /// nächste Änderung passiert. Der Boot-Call tauscht ConfigVersion ohnehin schon in
    /// beide Richtungen aus (Klassenkommentar) - dieses Event macht daraus zusätzlich zum
    /// bestehenden Programmversion-Vergleich auch einen Config-Pull-Trigger, symmetrisch:
    /// wer auch immer beim Austausch die niedrigere Version meldet, zieht sich die neuere
    /// vom jeweils anderen, unabhängig davon, wer den Boot-Call initiiert hat.
    /// </summary>
    public event EventHandler<PeerConfigVersionInfo>? PeerConfigVersionObserved;

    /// <param name="discoveryPort">Overridable only for tests - production always uses <see cref="AppConstants.DiscoveryUdpPort"/> so every device agrees on one port.</param>
    /// <param name="bridgeSeedAddressProvider">
    /// Multi-VLAN-Bridge-Seed (siehe Klassenkommentar): liefert bei jedem Announce-Aufruf
    /// die aktuell konfigurierte Bootstrap-Adresse (leer/null = keine, Normalfall). Ein
    /// Func statt eines festen Werts, weil der Aufrufer (HaelpMi.Agent) sie hot-reload-fähig
    /// aus SharedConfig lesen soll, nicht einmalig beim Konstruieren einfrieren darf.
    /// </param>
    public DiscoveryService(
        Func<LiveIdentity> identityProvider,
        Action<string>? audit = null,
        int? discoveryPort = null,
        Func<string?>? bridgeSeedAddressProvider = null)
    {
        _identityProvider = identityProvider;
        _audit = audit;
        _discoveryPort = discoveryPort ?? AppConstants.DiscoveryUdpPort;
        _bridgeSeedAddressProvider = bridgeSeedAddressProvider;
    }

    /// <summary>Binds the socket and starts the background receive loop. Call once at Agent startup.</summary>
    public void StartListening()
    {
        if (_socket is not null)
        {
            return;
        }

        _socket = new UdpClient
        {
            EnableBroadcast = true,
        };
        _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));

        _cts = new CancellationTokenSource();
        _receiveLoop = ReceiveLoopAsync(_socket, _cts.Token);
    }

    /// <summary>Sends the once-per-startup boot-call announce (Teil 2, Abschnitt 9), or a manual "Erneut suchen" (FR-20).</summary>
    public async Task AnnounceAsync(CancellationToken ct = default)
    {
        if (_socket is null)
        {
            throw new InvalidOperationException($"{nameof(StartListening)} must be called first.");
        }

        var message = BuildMessage(MessageKind.Announce);
        var payload = NetworkSerializer.ToUtf8Json(message);
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
        await _socket.SendAsync(payload, payload.Length, broadcastEndpoint).WaitAsync(ct);

        await SendToBridgeSeedAsync(payload, ct);
    }

    /// <summary>
    /// Multi-VLAN-Bridge-Seed (Klassenkommentar): zusätzlicher Unicast desselben Announce-
    /// Payloads an eine konfigurierte Bootstrap-Adresse jenseits des eigenen Broadcast-
    /// Bereichs. Bewusst best-effort und komplett getrennt vom obigen Broadcast-Send in
    /// AnnounceAsync - eine falsche/nicht (mehr) erreichbare Adresse (DNS-Fehler, Timeout,
    /// falsch getippt) darf niemals die normale lokale Discovery beeinträchtigen, die zu
    /// diesem Zeitpunkt bereits erfolgreich rausgegangen ist.
    /// </summary>
    private async Task SendToBridgeSeedAsync(byte[] payload, CancellationToken ct)
    {
        var seedAddress = _bridgeSeedAddressProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(seedAddress) || _socket is null)
        {
            return;
        }

        try
        {
            // IP direkt (Normalfall bei einer festen Standort-zu-Standort-Route) oder
            // Hostname (falls das Kundennetz DNS über die VPN-Verbindung anbietet) - beides
            // erlaubt, ohne dass der Admin im Dashboard zwischen beiden unterscheiden muss.
            var resolved = IPAddress.TryParse(seedAddress, out var parsed)
                ? parsed
                : (await Dns.GetHostAddressesAsync(seedAddress, ct)).FirstOrDefault();
            if (resolved is null)
            {
                return;
            }

            var seedEndpoint = new IPEndPoint(resolved, _discoveryPort);
            await _socket.SendAsync(payload, payload.Length, seedEndpoint).WaitAsync(ct);
        }
        catch (Exception)
        {
            // best-effort - siehe Methodenkommentar; kein Audit-Log-Eintrag nötig, das würde
            // bei einer dauerhaft falsch konfigurierten Adresse nur bei jedem Announce erneut
            // spammen, ohne dass der Admin etwas Neues erfährt.
        }
    }

    private BootCallMessage BuildMessage(MessageKind kind, IReadOnlyList<KnownDeviceSummary>? knownDevices = null)
    {
        var identity = _identityProvider();
        return new BootCallMessage(
            kind,
            identity.CustomerGroupId,
            identity.DeviceId,
            identity.ComputerName,
            identity.User,
            identity.RoomName,
            identity.RoomNumber,
            identity.Role,
            identity.IsRemoteSession,
            AppConstants.AlarmTcpPort,
            identity.ProgramVersion,
            identity.ConfigVersion,
            DateTimeOffset.UtcNow,
            knownDevices);
    }

    private async Task ReceiveLoopAsync(UdpClient socket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            try
            {
                await HandleDatagramAsync(result, ct);
            }
            catch (Exception)
            {
                // A single bad/unexpected datagram must never take down discovery for the
                // rest of the process's lifetime (NFR-1: no planned downtime).
            }
        }
    }

    // War 4 KB (reine Identität passt locker rein) - Nutzerwunsch 05.08.2026: eine Reply
    // kann jetzt zusätzlich die komplette Geräteliste des Antwortenden anhängen (Gossip),
    // dafür großzügiger bemessen. Ein einzelner KnownDeviceSummary-Eintrag liegt bei grob
    // 150-200 Byte JSON, 32 KB deckt damit einige Hundert Geräte ab - für ein LAN-
    // Alarmsystem in Gebäudegröße realistisch mehr als genug, ohne UDP-Fragmentierung
    // riskant groß zu werden. ReplyDirectlyAsync fällt selbst auf eine Antwort ohne
    // Gossip-Anhang zurück, falls die eigene Liste ausnahmsweise doch nicht hineinpasst.
    private const int MaxDatagramBytes = 32 * 1024;

    private async Task HandleDatagramAsync(UdpReceiveResult result, CancellationToken ct)
    {
        if (result.Buffer.Length > MaxDatagramBytes)
        {
            return; // untrusted network input (CLAUDE.md): reject oversized datagrams before even parsing
        }

        BootCallMessage? message;
        try
        {
            message = NetworkSerializer.FromUtf8Json<BootCallMessage>(result.Buffer);
        }
        catch (Exception)
        {
            return; // malformed datagram - ignore, no partial trust in an admin-less network (NFR-6)
        }

        if (message is null || !message.IsPlausible())
        {
            return;
        }

        var ownIdentity = _identityProvider();

        if (!CustomerGroupFilter.Matches(message.CustomerGroupId, ownIdentity.CustomerGroupId))
        {
            return; // different deployment sharing the same physical network (Teil 2, Abschnitt 6) - not our traffic
        }

        if (message.DeviceId == ownIdentity.DeviceId)
        {
            return; // our own announce looping back on a multi-homed/broadcast-echo host
        }

        var remoteIp = result.RemoteEndPoint.Address.ToString();
        DeviceEntry updated;
        var learnedNewDevice = false;

        await _storeLock.WaitAsync(ct);
        try
        {
            var devices = _deviceStore.Load();
            var isNewPrimaryDevice = devices.All(d => d.DeviceId != message.DeviceId);
            var info = new DeviceUpsertInfo(
                message.ComputerName, message.User, message.RoomName, message.RoomNumber,
                message.Role, message.IsRemoteSession, remoteIp, message.TcpPort);
            DeviceStore.Upsert(devices, message.DeviceId, info, DateTimeOffset.UtcNow);
            updated = devices.First(d => d.DeviceId == message.DeviceId);
            learnedNewDevice = isNewPrimaryDevice;

            // Gossip (Nutzerwunsch 05.08.2026): eine Reply kann die komplette Geräteliste
            // des Antwortenden mitbringen - so lernen wir auch von Geräten, die gerade
            // offline sind und daher nie selbst direkt geantwortet hätten. Weder uns selbst
            // noch den gerade schon oben verarbeiteten Antwortenden nochmal eintragen.
            if (message.Kind == MessageKind.Reply && message.KnownDevices is { Count: > 0 } knownDevices)
            {
                foreach (var known in knownDevices)
                {
                    if (known.DeviceId == ownIdentity.DeviceId || known.DeviceId == message.DeviceId)
                    {
                        continue;
                    }

                    if (devices.All(d => d.DeviceId != known.DeviceId))
                    {
                        learnedNewDevice = true;
                    }

                    var knownInfo = new DeviceUpsertInfo(
                        known.ComputerName, known.User, known.RoomName, known.RoomNumber,
                        known.Role, false, known.IpAddress, known.TcpPort);
                    DeviceStore.Upsert(devices, known.DeviceId, knownInfo, DateTimeOffset.UtcNow);
                }
            }

            _deviceStore.Save(devices);
        }
        finally
        {
            _storeLock.Release();
        }

        _audit?.Invoke($"discovery {message.Kind} deviceId={message.DeviceId}");
        DeviceUpdated?.Invoke(this, updated);

        if (learnedNewDevice)
        {
            // Multi-VLAN-Bridge-Seed-Folgefix (Nutzerwunsch 13.08.2026): ohne das hier
            // erführen bereits laufende lokale Peers von einem neu über die Brücke gelernten
            // Fremdsubnetz-Gerät erst bei ihrem eigenen nächsten Boot (kein Heartbeat) oder
            // einem manuellen "Erneut suchen" - dieselbe Fehlerklasse wie der
            // PeerConfigVersionObserved-Fix vom 11.08.2026. Best-effort, eigener Re-Announce
            // löst wieder ganz normal Replies+Gossip bei den lokalen Nachbarn aus.
            _ = ReAnnounceBestEffortAsync(ct);
        }

        if (message.ProgramVersion != ownIdentity.ProgramVersion)
        {
            PeerVersionObserved?.Invoke(this, new PeerVersionInfo
            {
                DeviceId = message.DeviceId,
                ProgramVersion = message.ProgramVersion,
                ConfigVersion = message.ConfigVersion,
            });
        }

        if (message.ConfigVersion > ownIdentity.ConfigVersion)
        {
            PeerConfigVersionObserved?.Invoke(this, new PeerConfigVersionInfo
            {
                DeviceId = message.DeviceId,
                ConfigVersion = message.ConfigVersion,
            });
        }

        if (message.Kind == MessageKind.Announce)
        {
            await ReplyDirectlyAsync(result.RemoteEndPoint, message.DeviceId, ct);
        }
    }

    private async Task ReAnnounceBestEffortAsync(CancellationToken ct)
    {
        try
        {
            await AnnounceAsync(ct);
        }
        catch (Exception)
        {
            // best-effort, wie ReceiveLoopAsync's Toleranz für einen einzelnen fehlgeschlagenen
            // Schritt - der nächste eigene Boot bzw. ein manuelles "Erneut suchen" bleibt der
            // Fallback, falls ausgerechnet dieser eine Re-Announce scheitert.
        }
    }

    private async Task ReplyDirectlyAsync(IPEndPoint remoteEndpoint, Guid announcerDeviceId, CancellationToken ct)
    {
        if (_socket is null)
        {
            return;
        }

        var knownDevices = await BuildKnownDevicesSummaryAsync(announcerDeviceId, ct);
        var reply = BuildMessage(MessageKind.Reply, knownDevices);
        var payload = NetworkSerializer.ToUtf8Json(reply);
        if (payload.Length > MaxDatagramBytes)
        {
            // Sicherheitsnetz: die eigene Geräteliste ist (noch) größer, als in ein
            // Datagramm passt - lieber ohne Gossip-Anhang antworten als das Reply komplett
            // zu verlieren (der Empfänger würde ein Datagramm über MaxDatagramBytes
            // ohnehin verwerfen, siehe oben).
            reply = BuildMessage(MessageKind.Reply);
            payload = NetworkSerializer.ToUtf8Json(reply);
        }

        try
        {
            // Reply to the exact endpoint the announce came from - in production this is
            // always the announcer's fixed discovery port too, but relying on the observed
            // source endpoint (rather than re-assuming our own port) is simply correct UDP
            // request/reply behavior and is what makes this independently testable.
            await _socket.SendAsync(payload, payload.Length, remoteEndpoint).WaitAsync(ct);
        }
        catch (SocketException)
        {
            // best-effort: if this fails the announcer still has our earlier state (if any)
            // and can recover via "Erneut suchen" (FR-20)
        }
    }

    private async Task<IReadOnlyList<KnownDeviceSummary>> BuildKnownDevicesSummaryAsync(Guid excludeDeviceId, CancellationToken ct)
    {
        await _storeLock.WaitAsync(ct);
        List<DeviceEntry> devices;
        try
        {
            devices = _deviceStore.Load();
        }
        finally
        {
            _storeLock.Release();
        }

        return devices
            .Where(d => d.DeviceId != excludeDeviceId)
            .Select(d => new KnownDeviceSummary(d.DeviceId, d.ComputerName, d.User, d.RoomName, d.RoomNumber, d.Role, d.IpAddress, d.TcpPort))
            .ToList();
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _socket?.Dispose();
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop; } catch { /* already handled inside the loop */ }
        }
        _cts?.Dispose();
        _storeLock.Dispose();
    }
}
