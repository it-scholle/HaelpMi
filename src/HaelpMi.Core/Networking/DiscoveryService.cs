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

/// <summary>Siehe <see cref="DiscoveryService.PeerLicenseObserved"/>.</summary>
public sealed class PeerLicenseInfo
{
    public required Guid DeviceId { get; init; }
    public required string LicenseKeyText { get; init; }
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
/// Known limitation carried over from 5.5: a plain limited broadcast (255.255.255.255)
/// only reaches the default-route subnet, matching the spec's accepted "same
/// subnet/VLAN only" constraint - segmented networks fall back to manual entries.
/// </summary>
public sealed class DiscoveryService : IAsyncDisposable
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Func<string?>? _ownLicenseKeyTextProvider;
    private readonly int _discoveryPort;
    private readonly DeviceStore _deviceStore = new();
    private readonly SemaphoreSlim _storeLock = new(1, 1);
    private readonly Action<string>? _audit;
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

    /// <summary>
    /// Issue #59/#60-Nachtrag "Lizenz sofort verteilen": feuert für JEDEN Boot-Call, der
    /// eine Lizenz mitbringt (<see cref="BootCallMessage.LicenseKeyText"/>) - bewusst OHNE
    /// Vorfilterung "ist die neuer als meine eigene", anders als bei
    /// <see cref="PeerConfigVersionObserved"/>: DiscoveryService kennt weder den
    /// Prüfschlüssel noch die eigene aktuell geladene Lizenz, die Entscheidung "übernehmen
    /// oder verwerfen" liegt komplett beim Abonnenten (siehe LicenseImporter.TryAdoptFromPeer).
    /// </summary>
    public event EventHandler<PeerLicenseInfo>? PeerLicenseObserved;

    /// <param name="discoveryPort">Overridable only for tests - production always uses <see cref="AppConstants.DiscoveryUdpPort"/> so every device agrees on one port.</param>
    /// <param name="ownLicenseKeyTextProvider">
    /// Issue #59/#60-Nachtrag: liefert die eigene, aktuell geladene Lizenz als Text (oder
    /// null, falls keine vorliegt) - wird an jeden ausgehenden Boot-Call angehängt, damit
    /// Peers ohne (aktuelle) Lizenz sie übernehmen können. Optional, damit bestehende
    /// Aufrufer/Tests unverändert kompilieren.
    /// </param>
    public DiscoveryService(Func<LiveIdentity> identityProvider, Action<string>? audit = null, int? discoveryPort = null, Func<string?>? ownLicenseKeyTextProvider = null)
    {
        _identityProvider = identityProvider;
        _audit = audit;
        _discoveryPort = discoveryPort ?? AppConstants.DiscoveryUdpPort;
        _ownLicenseKeyTextProvider = ownLicenseKeyTextProvider;
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
            knownDevices,
            identity.FirstSeenUtc,
            _ownLicenseKeyTextProvider?.Invoke());
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

        await _storeLock.WaitAsync(ct);
        try
        {
            var devices = _deviceStore.Load();
            var info = new DeviceUpsertInfo(
                message.ComputerName, message.User, message.RoomName, message.RoomNumber,
                message.Role, message.IsRemoteSession, remoteIp, message.TcpPort, message.FirstSeenUtc);
            DeviceStore.Upsert(devices, message.DeviceId, info, DateTimeOffset.UtcNow);
            updated = devices.First(d => d.DeviceId == message.DeviceId);

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

                    var knownInfo = new DeviceUpsertInfo(
                        known.ComputerName, known.User, known.RoomName, known.RoomNumber,
                        known.Role, false, known.IpAddress, known.TcpPort, known.FirstSeenUtc);
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
        RaiseObserver(() => DeviceUpdated?.Invoke(this, updated), nameof(DeviceUpdated));

        if (message.ProgramVersion != ownIdentity.ProgramVersion)
        {
            RaiseObserver(() => PeerVersionObserved?.Invoke(this, new PeerVersionInfo
            {
                DeviceId = message.DeviceId,
                ProgramVersion = message.ProgramVersion,
                ConfigVersion = message.ConfigVersion,
            }), nameof(PeerVersionObserved));
        }

        if (message.LicenseKeyText is { Length: > 0 })
        {
            RaiseObserver(() => PeerLicenseObserved?.Invoke(this, new PeerLicenseInfo
            {
                DeviceId = message.DeviceId,
                LicenseKeyText = message.LicenseKeyText,
            }), nameof(PeerLicenseObserved));
        }

        if (message.ConfigVersion > ownIdentity.ConfigVersion)
        {
            RaiseObserver(() => PeerConfigVersionObserved?.Invoke(this, new PeerConfigVersionInfo
            {
                DeviceId = message.DeviceId,
                ConfigVersion = message.ConfigVersion,
            }), nameof(PeerConfigVersionObserved));
        }

        // Muss auf jeden Fall laufen, unabhängig davon, ob einer der obigen Beobachter
        // (insbesondere PeerLicenseObserved - Datei-I/O + Signaturprüfung im Abonnenten,
        // siehe HaelpMi.Agent) fehlgeschlagen ist - siehe RaiseObserver-Begründung.
        if (message.Kind == MessageKind.Announce)
        {
            await ReplyDirectlyAsync(result.RemoteEndPoint, message.DeviceId, ct);
        }
    }

    /// <summary>
    /// Bugfix (Fehlerbericht 07.09.2026, "Config kommt nach Lizenz-Freischaltung nicht mehr
    /// an"): ein Abonnent von <see cref="PeerLicenseObserved"/> (HaelpMi.Agent, Datei-I/O +
    /// Signaturprüfung) kann fehlschlagen - eine ungefangene Exception dort hätte bisher die
    /// GESAMTE restliche Verarbeitung DIESES Boot-Calls abgebrochen, inklusive
    /// <see cref="PeerConfigVersionObserved"/> und der Antwort in <see cref="ReplyDirectlyAsync"/>
    /// (beide standen im Code danach). Jedes Beobachter-Event bekommt jetzt seinen eigenen
    /// Fehlerkreis - ein kaputter Abonnent verliert nur sein eigenes Signal, nie die Signale
    /// der anderen oder die Boot-Call-Antwort selbst (gleiche NFR-1-Haltung wie beim äußeren
    /// Fang in ReceiveLoopAsync, nur granularer statt den ganzen Datagramm-Durchlauf zu opfern).
    /// </summary>
    private void RaiseObserver(Action raise, string observerName)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            _audit?.Invoke($"discovery observer '{observerName}' fehlgeschlagen: {ex.Message}");
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
            .Select(d => new KnownDeviceSummary(d.DeviceId, d.ComputerName, d.User, d.RoomName, d.RoomNumber, d.Role, d.IpAddress, d.TcpPort, d.FirstSeenUtc))
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
