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

/// <summary>Siehe <see cref="DiscoveryService.OwnLicenseOverrideObserved"/>.</summary>
public sealed class OwnLicenseOverrideInfo
{
    public required LicenseOverride Override { get; init; }
    public required DateTimeOffset SetAtUtc { get; init; }
}

/// <summary>
/// Siehe <see cref="DiscoveryService.OwnDeviceRemovedObserved"/>. <see cref="Removed"/> ist
/// (Issue #61-Nachtrag 08.09.2026 "Deinstalliert" statt Verstecken) bewusst kein reines
/// "immer true" mehr wie in der ursprünglichen Fassung: eine Neuinstallation kann die
/// Markierung jetzt auch wieder auf <c>false</c> setzen (siehe DeviceStore.Upsert,
/// LastInstalledAtUtc-Regel), dieses Event trägt beide Richtungen.
/// </summary>
public sealed class OwnDeviceRemovedInfo
{
    public required bool Removed { get; init; }
    public required DateTimeOffset SetAtUtc { get; init; }
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
    private readonly RemovedDeviceStore _removedDeviceStore = new();
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

    /// <summary>
    /// Issue #61-Nachtrag (Propagierungs-Bugfix 08.09.2026): eine im Geräte-Tab getroffene
    /// Admin-Entscheidung ÜBER DIESES Gerät, gelernt aus dem Gossip-Anhang einer
    /// Boot-Call-Nachricht (Announce mit <c>includeKnownDevices: true</c> ODER Reply). Ein
    /// Gossip-Eintrag über die eigene DeviceId wird für jedes andere Feld (Computername/
    /// Raum/Rolle/...) weiterhin ignoriert - ein Gerät kennt sich selbst besser als jeder
    /// Dritte - aber genau der Override ist eine legitime Ausnahme: er stammt normalerweise
    /// nie vom betroffenen Gerät selbst, ein Gerät erfährt ihn also meist nur über einen
    /// Dritten. Der bisherige blanke "Gossip über mich selbst wird ignoriert"-Filter hat das
    /// versehentlich mit verworfen - <see cref="HaelpMi.Core.Licensing.LicenseLimitGuard"/>
    /// hat dadurch nie erfahren, wenn genau das eigene Gerät (de-)aktiviert wurde. Seit Issue
    /// #113 fließt hier auch der auf None/ForceDisabled beschränkte Selbstbericht eines
    /// Admin-Geräts über sich selbst ein (<see cref="AnnounceSelfLicenseOverrideAsync"/>),
    /// technisch derselbe generische "Sonderfall Selbstmeldung"-Pfad wie bei
    /// <see cref="OwnDeviceRemovedObserved"/>.
    /// </summary>
    public event EventHandler<OwnLicenseOverrideInfo>? OwnLicenseOverrideObserved;

    /// <summary>
    /// Issue #61-Nachtrag (Fehlerbericht "Löschen im Geräte-Tab deaktiviert das Gerät
    /// nicht wirklich"): eine im Geräte-Tab getroffene "Löschen"-Entscheidung ÜBER DIESES
    /// Gerät, gelernt aus dem Gossip-Anhang einer Boot-Call-Nachricht - genau derselbe
    /// Lernpfad wie <see cref="OwnLicenseOverrideObserved"/>, nur einseitig (kann nur von
    /// "nicht entfernt" auf "entfernt" wechseln, siehe RemovedDeviceStore).
    /// </summary>
    public event EventHandler<OwnDeviceRemovedInfo>? OwnDeviceRemovedObserved;

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

    /// <summary>
    /// Sends the once-per-startup boot-call announce (Teil 2, Abschnitt 9), or a manual
    /// "Erneut suchen" (FR-20).
    ///
    /// <paramref name="includeKnownDevices"/> (Issue #61-Nachtrag 08.09.2026): normalerweise
    /// false (schlanker, häufiger Announce - reine Identität reicht). Ein bewusst
    /// admin-/nutzer-ausgelöstes "Erneut suchen" (nie ein stiller Hintergrund-Trigger, siehe
    /// Aufrufer) setzt es auf true und hängt die komplette eigene Geräteliste als Gossip an,
    /// genau wie sonst nur eine Reply es könnte (siehe <see cref="BuildKnownDevicesSummaryAsync"/>) -
    /// broadcastet an ALLE gerade lauschenden Geräte auf einen Schlag, statt darauf zu
    /// warten, dass jedes betroffene Gerät irgendwann von sich aus selbst announct. Behebt
    /// den Fall, dass eine im Geräte-Tab getroffene Aktivieren/Deaktivieren-Entscheidung das
    /// betroffene Gerät sonst erst bei dessen eigenem nächsten Boot-Call erreicht hätte.
    /// </summary>
    public async Task AnnounceAsync(CancellationToken ct = default, bool includeKnownDevices = false)
    {
        if (_socket is null)
        {
            throw new InvalidOperationException($"{nameof(StartListening)} must be called first.");
        }

        IReadOnlyList<KnownDeviceSummary>? knownDevices = null;
        IReadOnlyList<PermanentlyRemovedDeviceSummary>? permanentlyRemovedDevices = null;
        if (includeKnownDevices)
        {
            (knownDevices, permanentlyRemovedDevices) = await BuildKnownDevicesSummaryAsync(Guid.Empty, ct);
        }

        var message = BuildMessage(MessageKind.Announce, knownDevices, permanentlyRemovedDevices);
        var payload = NetworkSerializer.ToUtf8Json(message);
        if (payload.Length > MaxDatagramBytes)
        {
            // Sicherheitsnetz wie bei ReplyDirectlyAsync: lieber ohne Gossip-Anhang senden als das Announce komplett zu verlieren.
            message = BuildMessage(MessageKind.Announce);
            payload = NetworkSerializer.ToUtf8Json(message);
        }

        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
        await _socket.SendAsync(payload, payload.Length, broadcastEndpoint).WaitAsync(ct);
    }

    /// <summary>
    /// Issue #61-Nachtrag (Nutzerbericht 08.09.2026 "Löschen ist ein Freischein" - ein
    /// gelöschtes Gerät sendete weiter Alarme, ein anderes Admin-Dashboard zeigte es
    /// weiter als Ziel): der Broadcast in <see cref="AnnounceAsync"/> erreicht nur, wer
    /// GENAU in diesem Moment auf demselben Subnetz mithört - für eine Deaktivierung, die
    /// zumindest das betroffene Gerät SELBST zuverlässig erreichen muss (Nutzerentscheidung
    /// 08.09.2026: "die Reaktion des Systems muss mindestens am Endgerät ankommen, auch
    /// wenn alles andere nicht"), reicht das nicht. Kontaktiert deshalb zusätzlich jede
    /// bekannte IP direkt per Unicast - sowohl aktuell bekannte Peers als auch gerade erst
    /// gelöschte (deren letzte bekannte IP nur noch in <see cref="RemovedDeviceStore"/>
    /// steht, siehe dortiger Kommentar) - derselbe Zuverlässigkeits-Ansatz wie beim
    /// eigentlichen Alarmversand (AlarmSender kontaktiert jedes Zielgerät ebenfalls direkt
    /// statt zu broadcasten). Best-effort pro Ziel: ein einzelnes nicht erreichbares Gerät
    /// darf die anderen nicht verhindern, und ein bereits informiertes Gerät übernimmt eine
    /// erneute, gleich alte Meldung ohnehin wirkungslos (siehe DeviceStore.Upsert, "neuester
    /// Zeitstempel gewinnt").
    /// </summary>
    public async Task NotifyKnownPeersDirectlyAsync(CancellationToken ct = default)
    {
        if (_socket is null)
        {
            return;
        }

        List<DeviceEntry> devices;
        List<RemovedDeviceEntry> removedDevices;
        await _storeLock.WaitAsync(ct);
        try
        {
            devices = _deviceStore.Load();
            removedDevices = _removedDeviceStore.Load();
        }
        finally
        {
            _storeLock.Release();
        }

        var targetIps = CollectNotifyTargetIps(devices, removedDevices);

        if (targetIps.Count == 0)
        {
            return;
        }

        var (knownDevices, permanentlyRemovedDevices) = await BuildKnownDevicesSummaryAsync(Guid.Empty, ct);
        var message = BuildMessage(MessageKind.Announce, knownDevices, permanentlyRemovedDevices);
        var payload = NetworkSerializer.ToUtf8Json(message);
        if (payload.Length > MaxDatagramBytes)
        {
            message = BuildMessage(MessageKind.Announce);
            payload = NetworkSerializer.ToUtf8Json(message);
        }

        foreach (var ip in targetIps)
        {
            if (!IPAddress.TryParse(ip, out var address))
            {
                continue;
            }

            try
            {
                await _socket.SendAsync(payload, payload.Length, new IPEndPoint(address, _discoveryPort)).WaitAsync(ct);
            }
            catch (SocketException)
            {
                // best-effort, wie ReplyDirectlyAsync - ein nicht erreichbares Gerät darf die übrigen Ziele nicht blockieren
            }
        }
    }

    /// <summary>
    /// Issue #61-Nachtrag (Nutzerwunsch 08.09.2026 "der Admin sollte im besten Fall auch
    /// gar nicht händisch deaktivieren müssen"): vom Uninstaller aufgerufen
    /// (<c>HaelpMi.Agent --notify-uninstall</c>, siehe installer/HaelpMiCommon.iss.inc
    /// [UninstallRun]), BEVOR die Programmdateien entfernt werden - meldet die eigene
    /// Deinstallation aktiv ans Netz, statt darauf zu warten, dass ein Admin sie manuell im
    /// Geräte-Tab nachträgt. Ein Gerät hat sonst nie eine legitime Gelegenheit, eine Meinung
    /// über die eigene DeviceId zu äußern (siehe DeviceUpsertInfo) - dieser eine Aufruf ist
    /// die bewusste Ausnahme, technisch über genau das Feld transportiert, das ein Dritter
    /// sonst für eine Fremdmeinung nutzt (<see cref="KnownDeviceSummary.Removed"/>), nur
    /// diesmal über sich selbst statt über einen anderen. Broadcast UND Direct-Unicast an
    /// jeden bisher bekannten Peer (gleicher Zuverlässigkeits-Ansatz wie
    /// <see cref="NotifyKnownPeersDirectlyAsync"/>) - dieser Prozess läuft nur diesen einen
    /// Moment lang, es gibt keine zweite Chance über einen späteren Boot-Call.
    /// </summary>
    public async Task AnnounceSelfRemovedAsync(CancellationToken ct = default)
    {
        if (_socket is null)
        {
            return;
        }

        var identity = _identityProvider();
        var removedAtUtc = DateTimeOffset.UtcNow;
        var selfSummary = new KnownDeviceSummary(
            identity.DeviceId, identity.ComputerName, identity.User, identity.RoomName, identity.RoomNumber,
            identity.Role, string.Empty, 0, identity.FirstSeenUtc, LicenseOverride.None, null,
            Removed: true, RemovedSetAtUtc: removedAtUtc);

        var message = BuildMessage(MessageKind.Announce, new[] { selfSummary });
        var payload = NetworkSerializer.ToUtf8Json(message);
        if (payload.Length > MaxDatagramBytes)
        {
            // Kann hier praktisch nie eintreten (ein einzelner Gossip-Eintrag), Sicherheitsnetz
            // trotzdem konsistent mit jedem anderen Sendepfad in dieser Klasse.
            message = BuildMessage(MessageKind.Announce);
            payload = NetworkSerializer.ToUtf8Json(message);
        }

        try
        {
            await _socket.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, _discoveryPort)).WaitAsync(ct);
        }
        catch (SocketException)
        {
            // best-effort - der direkte Unicast unten ist ohnehin die zuverlässigere Schiene
        }

        List<DeviceEntry> devices;
        await _storeLock.WaitAsync(ct);
        try
        {
            devices = _deviceStore.Load();
        }
        finally
        {
            _storeLock.Release();
        }

        foreach (var ip in devices.Select(d => d.IpAddress).Where(ip => !string.IsNullOrEmpty(ip)).Distinct())
        {
            if (!IPAddress.TryParse(ip, out var address))
            {
                continue;
            }

            try
            {
                await _socket.SendAsync(payload, payload.Length, new IPEndPoint(address, _discoveryPort)).WaitAsync(ct);
            }
            catch (SocketException)
            {
                // best-effort, wie NotifyKnownPeersDirectlyAsync - ein nicht erreichbares Gerät darf die übrigen Ziele nicht blockieren
            }
        }
    }

    /// <summary>
    /// Issue #113 (Admin kann sich selbst im Geräte-Tab deaktivieren): technisch derselbe
    /// Sonderweg wie <see cref="AnnounceSelfRemovedAsync"/> - broadcastet UND unicastet einen
    /// Selbstbericht über <see cref="LicenseOverride"/>, das normale Gossip-Feld, über das ein
    /// Gerät sonst nie legitim über sich selbst berichten kann (siehe DeviceUpsertInfo).
    /// Bewusst beschränkt auf <see cref="LicenseOverride.None"/> und
    /// <see cref="LicenseOverride.ForceDisabled"/> - <see cref="LicenseOverride.ForceEnabled"/>
    /// würde einem Gerät erlauben, sich selbst am Lizenzkontingent vorbeizumogeln, wenn diese
    /// Beschränkung hier jemals fiele. Aufrufer (HaelpMi.Agent, angestoßen vom Dashboard-Klick
    /// über IpcCommandType.OwnLicenseOverrideChanged) muss diese Einschränkung ebenfalls
    /// einhalten - siehe auch die spiegelbildliche Prüfung beim Empfang unten.
    /// </summary>
    public async Task AnnounceSelfLicenseOverrideAsync(LicenseOverride value, DateTimeOffset setAtUtc, CancellationToken ct = default)
    {
        if (_socket is null || value == LicenseOverride.ForceEnabled)
        {
            return;
        }

        var identity = _identityProvider();
        // TcpPort echt statt 0 (anders als bei AnnounceSelfRemovedAsync oben): dort ist ein
        // Port-loser Eintrag über Removed:true von der sonst geltenden Plausibilitätsprüfung
        // ausgenommen (siehe MessageValidation.IsPlausible), hier bliebe die gesamte Nachricht
        // ohne echten Port sonst als unplausibel verworfen.
        var selfSummary = new KnownDeviceSummary(
            identity.DeviceId, identity.ComputerName, identity.User, identity.RoomName, identity.RoomNumber,
            identity.Role, string.Empty, AppConstants.AlarmTcpPort, identity.FirstSeenUtc, value, setAtUtc);

        var message = BuildMessage(MessageKind.Announce, new[] { selfSummary });
        var payload = NetworkSerializer.ToUtf8Json(message);
        if (payload.Length > MaxDatagramBytes)
        {
            // Sicherheitsnetz wie AnnounceSelfRemovedAsync - kann bei einem einzelnen
            // Gossip-Eintrag praktisch nie eintreten.
            message = BuildMessage(MessageKind.Announce);
            payload = NetworkSerializer.ToUtf8Json(message);
        }

        try
        {
            await _socket.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, _discoveryPort)).WaitAsync(ct);
        }
        catch (SocketException)
        {
            // best-effort - der direkte Unicast unten ist ohnehin die zuverlässigere Schiene
        }

        List<DeviceEntry> devices;
        await _storeLock.WaitAsync(ct);
        try
        {
            devices = _deviceStore.Load();
        }
        finally
        {
            _storeLock.Release();
        }

        foreach (var ip in devices.Select(d => d.IpAddress).Where(ip => !string.IsNullOrEmpty(ip)).Distinct())
        {
            if (!IPAddress.TryParse(ip, out var address))
            {
                continue;
            }

            try
            {
                await _socket.SendAsync(payload, payload.Length, new IPEndPoint(address, _discoveryPort)).WaitAsync(ct);
            }
            catch (SocketException)
            {
                // best-effort, wie AnnounceSelfRemovedAsync - ein nicht erreichbares Gerät darf die übrigen Ziele nicht blockieren
            }
        }
    }

    /// <summary>
    /// Reine Ziel-Ermittlung für <see cref="NotifyKnownPeersDirectlyAsync"/>, als eigene
    /// pure Funktion herausgezogen, damit sie ohne echtes Netzwerk testbar ist: jede
    /// bekannte Peer-IP plus jede noch vorhandene letzte-bekannte-IP eines Tombstones,
    /// leere/fehlende IPs (Gerät nie wirklich erreicht) und Duplikate herausgefiltert.
    /// </summary>
    internal static List<string> CollectNotifyTargetIps(List<DeviceEntry> devices, List<RemovedDeviceEntry> removedDevices) =>
        devices.Select(d => d.IpAddress)
            .Concat(removedDevices.Select(r => r.LastKnownIpAddress ?? string.Empty))
            .Where(ip => !string.IsNullOrEmpty(ip))
            .Distinct()
            .ToList();

    private BootCallMessage BuildMessage(MessageKind kind, IReadOnlyList<KnownDeviceSummary>? knownDevices = null, IReadOnlyList<PermanentlyRemovedDeviceSummary>? permanentlyRemovedDevices = null)
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
            _ownLicenseKeyTextProvider?.Invoke(),
            identity.LastInstalledAtUtc,
            permanentlyRemovedDevices);
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
        DeviceEntry? updated = null;
        OwnLicenseOverrideInfo? ownOverrideInfo = null;
        OwnDeviceRemovedInfo? ownRemovedInfo = null;

        await _storeLock.WaitAsync(ct);
        try
        {
            var devices = _deviceStore.Load();
            var removedDevices = _removedDeviceStore.Load();

            // Issue #61-Nachtrag ("Löschen deaktiviert nicht wirklich"): ein per Tombstone
            // bereits als gelöscht bekannter Absender wird nie wieder aufgenommen, auch
            // wenn er selbst weiterhin ganz normal announct (er weiß von seiner eigenen
            // Löschung ja zunächst nichts) - siehe RemovedDeviceStore-Klassenkommentar.
            if (RemovedDeviceStore.Contains(removedDevices, message.DeviceId))
            {
                devices.RemoveAll(d => d.DeviceId == message.DeviceId);
            }
            else
            {
                // Kein Override/Removed-Fremdmeinung hier (bleiben null): ein Gerät berichtet
                // nie eine Meinung über sich selbst - siehe DeviceUpsertInfo. LastInstalledAtUtc
                // dagegen ist GENAU umgekehrt nur im Selbstbericht sinnvoll (Issue #61-Nachtrag) -
                // hebt eine hier lokal gespeicherte Removed-Markierung automatisch auf, falls
                // neuer als deren Zeitstempel (siehe DeviceStore.Upsert).
                var info = new DeviceUpsertInfo(
                    message.ComputerName, message.User, message.RoomName, message.RoomNumber,
                    message.Role, message.IsRemoteSession, remoteIp, message.TcpPort, message.FirstSeenUtc,
                    LastInstalledAtUtc: message.LastInstalledAtUtc);
                DeviceStore.Upsert(devices, message.DeviceId, info, DateTimeOffset.UtcNow);
                updated = devices.First(d => d.DeviceId == message.DeviceId);
            }

            // Gossip (Nutzerwunsch 05.08.2026): sowohl eine Reply als auch ein bewusst mit
            // includeKnownDevices ausgelöstes Announce (Issue #61-Nachtrag, siehe
            // AnnounceAsync) können die komplette Geräteliste des Absenders mitbringen - so
            // lernen wir auch von Geräten, die gerade offline sind und daher nie selbst
            // direkt geantwortet hätten.
            if (message.KnownDevices is { Count: > 0 } knownDevices)
            {
                foreach (var known in knownDevices)
                {
                    if (known.DeviceId == ownIdentity.DeviceId)
                    {
                        // Issue #61-Nachtrag: für jedes ANDERE Feld bleibt Gossip über die
                        // eigene DeviceId zurecht ignoriert (ein Gerät kennt sich selbst
                        // besser als jeder Dritte) - Override/Removed sind die einzigen
                        // legitimen Ausnahmen, da sie per Definition nie vom betroffenen
                        // Gerät selbst stammen können.
                        if (known.OverrideSetAtUtc is { } setAtUtc)
                        {
                            ownOverrideInfo = new OwnLicenseOverrideInfo { Override = known.Override, SetAtUtc = setAtUtc };
                        }

                        if (known.RemovedSetAtUtc is { } ownRemovedSetAtUtc)
                        {
                            ownRemovedInfo = new OwnDeviceRemovedInfo { Removed = known.Removed, SetAtUtc = ownRemovedSetAtUtc };
                        }

                        continue;
                    }

                    if (RemovedDeviceStore.Contains(removedDevices, known.DeviceId))
                    {
                        // Endgültig gelöscht (Issue #61) - lebt nie wieder auf, weder über
                        // eine normale Selbstauskunft noch über eine Drittmeinung mit
                        // Removed:false (siehe RemovedDeviceStore-Klassenkommentar).
                        devices.RemoveAll(d => d.DeviceId == known.DeviceId);
                        continue;
                    }

                    if (known.DeviceId == message.DeviceId)
                    {
                        // Sonderfall: ein Gerät kann eine Meinung über SICH SELBST nur über
                        // AnnounceSelfRemovedAsync (Deinstallations-Meldung) oder, seit Issue
                        // #113, AnnounceSelfLicenseOverrideAsync äußern - alle anderen Felder
                        // (Raum/Name/IP/...) wurden bereits oben aus dem eigentlichen
                        // Boot-Call-Umschlag übernommen (dort mit der wirklich beobachteten
                        // Quell-IP statt eines hier evtl. veralteten Werts), deshalb hier NUR
                        // die Removed-/Override-Fremdmeinung mergen, kein Upsert.
                        if (known.RemovedSetAtUtc is { } selfRemovedSetAtUtc && updated is not null
                            && (updated.RemovedSetAtUtc is null || selfRemovedSetAtUtc > updated.RemovedSetAtUtc))
                        {
                            updated.Removed = known.Removed;
                            updated.RemovedSetAtUtc = selfRemovedSetAtUtc;
                        }

                        // Issue #113: nur ein Admin-Gerät darf sich selbst deaktivieren
                        // (Dashboard bleibt trotzdem nutzbar, siehe DashboardAccessGuard), und
                        // nur auf None/ForceDisabled - ForceEnabled bleibt über eine
                        // Selbstmeldung ausgeschlossen (Missbrauchsschutz, siehe
                        // AnnounceSelfLicenseOverrideAsync für dieselbe Beschränkung auf der
                        // Sendeseite; hier zusätzlich, falls je ein Absender diese Regel
                        // umgeht).
                        if (message.Role == Role.Admin
                            && known.Override is LicenseOverride.None or LicenseOverride.ForceDisabled
                            && known.OverrideSetAtUtc is { } selfOverrideSetAtUtc && updated is not null
                            && (updated.LicenseOverrideSetAtUtc is null || selfOverrideSetAtUtc > updated.LicenseOverrideSetAtUtc))
                        {
                            updated.LicenseOverride = known.Override;
                            updated.LicenseOverrideSetAtUtc = selfOverrideSetAtUtc;
                        }

                        continue;
                    }

                    // Issue #61 (Override) / #61-Nachtrag (Removed): Drittwissen trägt eine
                    // Fremdmeinung mit (siehe KnownDeviceSummary) - Upsert wendet beide per
                    // "neuester Zeitstempel gewinnt" auf den lokalen Stand an.
                    var knownInfo = new DeviceUpsertInfo(
                        known.ComputerName, known.User, known.RoomName, known.RoomNumber,
                        known.Role, false, known.IpAddress, known.TcpPort, known.FirstSeenUtc,
                        known.Override, known.OverrideSetAtUtc,
                        known.RemovedSetAtUtc is not null ? known.Removed : null, known.RemovedSetAtUtc);
                    DeviceStore.Upsert(devices, known.DeviceId, knownInfo, DateTimeOffset.UtcNow);
                }
            }

            // Bugfix "gelöschtes Gerät taucht per Gossip wieder auf" 10.09.2026: die
            // erstmalige "Endgültig löschen"-Entscheidung bleibt admin-only (siehe
            // Config/App.xaml.cs DeleteDevice), aber ein von einem Peer über
            // PermanentlyRemovedDeviceSummary WEITERGETRAGENER Tombstone wird hier lokal
            // übernommen - sonst gilt die Löschung nie über den einen Admin hinaus, bei dem
            // ursprünglich geklickt wurde (ein Peer, der das betroffene, weiterhin
            // laufende Gerät vorher direkt kontaktiert, hätte es sonst einfach wieder
            // aufgenommen). Bewusst getrennt vom KnownDevices-Merge oben: eine bloße
            // Removed=true-Drittmeinung (siehe dort) darf NIE einen eigenen Tombstone
            // erzeugen, nur ein echter RemovedDeviceStore-Eintrag des Absenders.
            if (message.PermanentlyRemovedDevices is { Count: > 0 } tombstones)
            {
                var tombstonesChanged = false;
                foreach (var tombstone in tombstones)
                {
                    if (tombstone.DeviceId == ownIdentity.DeviceId)
                    {
                        // Ein Gerät trägt sich nie selbst in seine eigene Peer-/Tombstone-
                        // Liste ein - unbeaufsichtigt übernommen könnte sonst jeder
                        // ungeprüfte Peer die eigene Identität dauerhaft sperren (keine
                        // Admin-Signaturprüfung auf diesem Versionsstand, CLAUDE.md).
                        continue;
                    }

                    if (!RemovedDeviceStore.Contains(removedDevices, tombstone.DeviceId))
                    {
                        var lastKnownIp = devices.FirstOrDefault(d => d.DeviceId == tombstone.DeviceId)?.IpAddress;
                        RemovedDeviceStore.Add(removedDevices, tombstone.DeviceId, tombstone.RemovedAtUtc, lastKnownIp);
                        tombstonesChanged = true;
                    }

                    devices.RemoveAll(d => d.DeviceId == tombstone.DeviceId);
                }

                if (tombstonesChanged)
                {
                    _removedDeviceStore.Save(removedDevices);
                }
            }

            _deviceStore.Save(devices);
        }
        finally
        {
            _storeLock.Release();
        }

        _audit?.Invoke($"discovery {message.Kind} deviceId={message.DeviceId}");
        if (updated is not null)
        {
            RaiseObserver(() => DeviceUpdated?.Invoke(this, updated), nameof(DeviceUpdated));
        }

        if (ownOverrideInfo is not null)
        {
            RaiseObserver(() => OwnLicenseOverrideObserved?.Invoke(this, ownOverrideInfo), nameof(OwnLicenseOverrideObserved));
        }

        if (ownRemovedInfo is not null)
        {
            RaiseObserver(() => OwnDeviceRemovedObserved?.Invoke(this, ownRemovedInfo), nameof(OwnDeviceRemovedObserved));
        }

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

        var (knownDevices, permanentlyRemovedDevices) = await BuildKnownDevicesSummaryAsync(announcerDeviceId, ct);
        var reply = BuildMessage(MessageKind.Reply, knownDevices, permanentlyRemovedDevices);
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

    private async Task<(IReadOnlyList<KnownDeviceSummary> KnownDevices, IReadOnlyList<PermanentlyRemovedDeviceSummary> PermanentlyRemovedDevices)> BuildKnownDevicesSummaryAsync(Guid excludeDeviceId, CancellationToken ct)
    {
        await _storeLock.WaitAsync(ct);
        List<DeviceEntry> devices;
        List<RemovedDeviceEntry> removedDevices;
        try
        {
            devices = _deviceStore.Load();
            removedDevices = _removedDeviceStore.Load();
        }
        finally
        {
            _storeLock.Release();
        }

        var summaries = devices
            .Where(d => d.DeviceId != excludeDeviceId)
            .Select(d => new KnownDeviceSummary(
                d.DeviceId, d.ComputerName, d.User, d.RoomName, d.RoomNumber, d.Role, d.IpAddress, d.TcpPort,
                d.FirstSeenUtc, d.LicenseOverride, d.LicenseOverrideSetAtUtc, d.Removed, d.RemovedSetAtUtc))
            .ToList();

        // Issue #61-Nachtrag: "dem Empfänger nichts über sich selbst erzählen" (der obige
        // Where-Filter) gilt für jedes Feld - AUSSER Override/Removed, den einzigen
        // legitimen Fremdmeinungen über die eigene DeviceId (siehe
        // OwnLicenseOverrideObserved/OwnDeviceRemovedObserved). Ohne diese Ausnahme würde
        // ReplyDirectlyAsync einem gerade erst wieder announcenden Gerät niemals die für
        // genau dieses Gerät getroffene Entscheidung zurückmelden - der Filter oben
        // schließt dessen Eintrag ja komplett aus.
        if (devices.FirstOrDefault(d => d.DeviceId == excludeDeviceId) is { } excludedSelf
            && (excludedSelf.LicenseOverrideSetAtUtc is not null || excludedSelf.RemovedSetAtUtc is not null))
        {
            summaries.Add(new KnownDeviceSummary(
                excludedSelf.DeviceId, excludedSelf.ComputerName, excludedSelf.User, excludedSelf.RoomName,
                excludedSelf.RoomNumber, excludedSelf.Role, excludedSelf.IpAddress, excludedSelf.TcpPort,
                excludedSelf.FirstSeenUtc, excludedSelf.LicenseOverride, excludedSelf.LicenseOverrideSetAtUtc,
                excludedSelf.Removed, excludedSelf.RemovedSetAtUtc));
        }

        // Endgültig-gelöscht-Tombstones (Issue #61, "Endgültig löschen") - eigenes Feld
        // statt eines weiteren KnownDeviceSummary-Eintrags (Bugfix 10.09.2026, siehe
        // PermanentlyRemovedDeviceSummary) - IMMER an jeden Empfänger angehängt, auch an
        // den Betroffenen selbst, damit sich die endgültige Löschung im ganzen Kreis
        // durchsetzt.
        var tombstones = removedDevices
            .Select(r => new PermanentlyRemovedDeviceSummary(r.DeviceId, r.RemovedAtUtc))
            .ToList();

        return (summaries, tombstones);
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
