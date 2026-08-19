using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Security;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Config-Sync hot-reload (Teil 2, Abschnitt 10): "Speichern + Verbreiten automatisch
/// bei Blur eines gültig ausgefüllten Pflichtfelds, kein Save-Button." An Admin-
/// Dashboard calls <see cref="PublishAsync"/> after every such field commit; every
/// device (Admin and User role alike) listens for the resulting UDP announce and, if it
/// carries a newer <see cref="SharedConfig.ConfigVersion"/> than what's already applied,
/// pulls the full config directly from the announcing device over TCP and hot-reloads
/// it in-process - never a parallel-instance swap, that mechanism is reserved for
/// program updates (Abschnitt 11).
/// </summary>
public sealed class ConfigSyncService : IAsyncDisposable
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Func<List<DeviceEntry>> _deviceListProvider;
    private readonly SharedConfigStore _configStore = new();
    private readonly ConfigHistoryStore _historyStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly Action<string>? _audit;
    private readonly Func<string?>? _groupKeyProvider;
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    private UdpClient? _udpSocket;
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private Task? _udpReceiveLoop;
    private Task? _tcpAcceptLoop;

    /// <summary>Raised after a newer config has been pulled and applied - callers should re-register hotkeys, refresh UI, etc.</summary>
    public event EventHandler<SharedConfig>? ConfigApplied;

    /// <param name="groupKeyProvider">
    /// LAN-Verschlüsselung (CLAUDE.md "Lizenz &amp; Secrets"): siehe AlarmSender-
    /// Konstruktor. Gilt hier NUR für den TCP-Pull (Request+Response, trägt den
    /// eigentlichen Konfigurationsinhalt) - der UDP-Announce bleibt bewusst unverändert
    /// im Klartext, er trägt nur "Config-Version X geändert", keinen Inhalt (siehe Plan-
    /// Dokument).
    /// </param>
    public ConfigSyncService(Func<LiveIdentity> identityProvider, Func<List<DeviceEntry>> deviceListProvider, Action<string>? audit = null, Func<string?>? groupKeyProvider = null)
    {
        _identityProvider = identityProvider;
        _deviceListProvider = deviceListProvider;
        _audit = audit;
        _groupKeyProvider = groupKeyProvider;
    }

    // tcpPort-Override (06.08.2026) nur für Tests gedacht - ein Fixport ohne Override
    // ließe sich nicht kollisionsfrei testen (Konflikt mit einem echten, auf demselben
    // Entwicklungsrechner laufenden Agent), gleiches Muster wie beim port-Parameter der
    // drei Geschwisterklassen (AlarmTcpListener/AlarmFeedbackChannel/EditLockService).
    public void Start(int? tcpPort = null)
    {
        if (_udpSocket is not null)
        {
            return;
        }

        var resolvedTcpPort = tcpPort ?? AppConstants.ConfigSyncTcpPort;

        _udpSocket = new UdpClient { EnableBroadcast = true };
        _udpSocket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpSocket.Client.Bind(new IPEndPoint(IPAddress.Any, AppConstants.ConfigSyncUdpPort));

        _cts = new CancellationTokenSource();
        _udpReceiveLoop = UdpReceiveLoopAsync(_udpSocket, _cts.Token);

        try
        {
            _tcpListener = new TcpListener(IPAddress.Any, resolvedTcpPort);
            _tcpListener.Start();
            _tcpAcceptLoop = TcpAcceptLoopAsync(_tcpListener, _cts.Token);
        }
        catch (SocketException ex)
        {
            // Entdeckt 04.08.2026 ("Dashboard startet nicht", zweite Ursache nach dem
            // DeviceId-Fund): der Agent-Prozess läuft praktisch immer schon (Autostart) und
            // hält diesen TCP-Port bereits für DIESELBE Geräte-ID. Ein zweiter
            // ConfigSyncService im selben Config.exe-Prozess (Admin-Dashboard) kann ihn
            // dann nicht zusätzlich belegen - TcpListener kennt (anders als der UDP-Socket
            // oben) kein Mehrfach-Binden. Das ist kein Fehlerfall: der bereits laufende
            // Agent bedient eingehende Config-Pull-Anfragen für dieses Gerät schon; Senden
            // (AnnounceAsync/PublishAsync) läuft unabhängig über den UDP-Socket weiter.
            _tcpListener = null;
            _audit?.Invoke($"configsync: lokaler TCP-Port {resolvedTcpPort} bereits belegt (vermutlich durch den laufenden Agent) - dieser Prozess sendet weiterhin, nimmt aber keine eingehenden Pull-Anfragen an: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies <paramref name="mutate"/> to a working copy of the current config, saves
    /// it, records a history entry (for Undo), and broadcasts the version bump. Only
    /// ever called from an Admin-Dashboard - a User-role device has no path that calls this.
    /// </summary>
    public async Task<SharedConfig> PublishAsync(
        Func<SharedConfig, SharedConfig> mutate,
        EditScopeKind scopeKind,
        Guid scopeId,
        string fieldPath,
        string? oldValueDisplay,
        string? newValueDisplay,
        CancellationToken ct = default)
    {
        var current = _configStore.LoadOrCreate();
        var previousSnapshotJson = System.Text.Json.JsonSerializer.Serialize(current);

        var updated = mutate(current);
        updated.ConfigVersion = current.ConfigVersion + 1;
        _configStore.Save(updated);

        var identity = _identityProvider();
        var history = _historyStore.Load();
        ConfigHistoryStore.Append(history, new ConfigHistoryEntry
        {
            ScopeKind = scopeKind,
            ScopeId = scopeId,
            ChangedAtUtc = DateTimeOffset.UtcNow,
            ChangedByUser = identity.User,
            ChangedByDeviceId = identity.DeviceId,
            FieldPath = fieldPath,
            OldValue = oldValueDisplay,
            NewValue = newValueDisplay,
            ResultingConfigVersion = updated.ConfigVersion,
            PreviousConfigSnapshotJson = previousSnapshotJson,
        });
        _historyStore.Save(history);

        ApplyToSelf(updated);
        await AnnounceAsync(updated.ConfigVersion, ct);

        return updated;
    }

    /// <summary>Reverts the most recent change for a Gruppe/Alarm-Profil by re-publishing its pre-change snapshot (Teil 2, Abschnitt 10: Undo).</summary>
    public async Task<bool> UndoLastChangeAsync(EditScopeKind scopeKind, Guid scopeId, CancellationToken ct = default)
    {
        var history = _historyStore.Load();
        var lastForScope = ConfigHistoryStore.ForScope(history, scopeKind, scopeId).FirstOrDefault();
        if (lastForScope is null)
        {
            return false;
        }

        var restored = System.Text.Json.JsonSerializer.Deserialize<SharedConfig>(lastForScope.PreviousConfigSnapshotJson);
        if (restored is null)
        {
            return false;
        }

        await PublishAsync(_ => restored, scopeKind, scopeId, $"Undo: {lastForScope.FieldPath}", lastForScope.NewValue, lastForScope.OldValue, ct);

        history.RemoveAll(e => e.Id == lastForScope.Id);
        _historyStore.Save(history);
        return true;
    }

    private async Task AnnounceAsync(int configVersion, CancellationToken ct)
    {
        if (_udpSocket is null)
        {
            return;
        }

        var identity = _identityProvider();
        var announce = new ConfigSyncAnnounceMessage(identity.CustomerGroupId, identity.DeviceId, configVersion, DateTimeOffset.UtcNow);
        var payload = NetworkSerializer.ToUtf8Json(announce);
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, AppConstants.ConfigSyncUdpPort);
        await _udpSocket.SendAsync(payload, payload.Length, broadcastEndpoint).WaitAsync(ct);
        TestLogger.LogAction(TestLogEventType.MessageSent, TestLogLevel.Info, TestLogDirection.Send,
            identity.DeviceId, detail: $"ConfigSyncAnnounce v{configVersion}");
    }

    private void ApplyToSelf(SharedConfig config)
    {
        var settings = _settingsStore.Load();
        settings.AppliedConfigVersion = config.ConfigVersion;

        if (config.DeviceAssignments.TryGetValue(settings.DeviceId, out var assignment))
        {
            settings.RoomName = assignment.RoomName;
            settings.RoomNumber = assignment.RoomNumber;

            // Explicit per-device override wins; if none is set, leave whatever sound was
            // already set (kein Gruppen-Standardton - siehe EditScope.cs für den Hintergrund).
            settings.IncomingSoundId = assignment.IncomingSoundId ?? settings.IncomingSoundId;
        }

        _settingsStore.Save(settings);
        TestLogger.LogAction(TestLogEventType.StatusChanged, TestLogLevel.Info, TestLogDirection.Local,
            settings.DeviceId, detail: $"ConfigApplied v{config.ConfigVersion}");
        ConfigApplied?.Invoke(this, config);
    }

    private const int MaxDatagramBytes = 4 * 1024;

    private async Task UdpReceiveLoopAsync(UdpClient socket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            try
            {
                await HandleAnnounceAsync(result, ct);
            }
            catch (Exception)
            {
                // one bad announce must never take down config-sync for the rest of the process's lifetime
            }
        }
    }

    private async Task HandleAnnounceAsync(UdpReceiveResult result, CancellationToken ct)
    {
        if (result.Buffer.Length > MaxDatagramBytes)
        {
            return;
        }

        ConfigSyncAnnounceMessage? announce;
        try
        {
            announce = NetworkSerializer.FromUtf8Json<ConfigSyncAnnounceMessage>(result.Buffer);
        }
        catch (Exception)
        {
            return;
        }

        if (announce is null || announce.OriginDeviceId == Guid.Empty)
        {
            return;
        }

        var identity = _identityProvider();
        if (!CustomerGroupFilter.Matches(announce.CustomerGroupId, identity.CustomerGroupId))
        {
            return; // Teil 2, Abschnitt 6
        }

        if (announce.OriginDeviceId == identity.DeviceId)
        {
            return; // our own broadcast looping back
        }

        TestLogger.LogAction(TestLogEventType.MessageReceived, TestLogLevel.Info, TestLogDirection.Receive,
            identity.DeviceId, remoteDeviceId: announce.OriginDeviceId, detail: $"ConfigSyncAnnounce v{announce.ConfigVersion}");
        await EvaluateAndPullAsync(announce.OriginDeviceId, announce.ConfigVersion, ct);
    }

    /// <summary>An <see cref="DiscoveryService.PeerConfigVersionObserved"/> hängen (Agent-Verdrahtung).</summary>
    public void OnPeerConfigVersionObserved(object? sender, PeerConfigVersionInfo info) =>
        _ = EvaluateAndPullAsync(info.DeviceId, info.ConfigVersion, CancellationToken.None);

    /// <summary>
    /// Gemeinsamer Kern für zwei Auslöser: den dedizierten Config-Sync-Broadcast
    /// (<see cref="HandleAnnounceAsync"/>, ausgelöst bei jeder Admin-Änderung - erreicht
    /// nur Geräte, die zu diesem Zeitpunkt schon liefen) und den Boot-Call-Austausch
    /// (<see cref="OnPeerConfigVersionObserved"/> - schließt die Lücke für ein Gerät, das
    /// erst NACH der letzten Config-Änderung gestartet/frisch installiert wurde und das
    /// damalige Announce nie gehört hat; Fehlerbericht 11.08.2026: drei frisch installierte
    /// Geräte blieben leer, bis der Admin eine weitere Änderung vorgenommen hat).
    /// </summary>
    private async Task EvaluateAndPullAsync(Guid originDeviceId, int remoteConfigVersion, CancellationToken ct)
    {
        var settings = _settingsStore.Load();
        if (remoteConfigVersion <= settings.AppliedConfigVersion)
        {
            TestLogger.LogAction(TestLogEventType.ActionSkipped, TestLogLevel.Info, TestLogDirection.Local,
                settings.DeviceId, remoteDeviceId: originDeviceId, detail: $"veraltete/gleiche Version v{remoteConfigVersion} <= v{settings.AppliedConfigVersion}");
            return; // already current or stale - nothing to do
        }

        var originDevice = _deviceListProvider().FirstOrDefault(d => d.DeviceId == originDeviceId);
        if (originDevice is null)
        {
            _audit?.Invoke($"configsync: newer version reported by unknown device={originDeviceId} - awaiting discovery");
            TestLogger.LogAction(TestLogEventType.ActionSkipped, TestLogLevel.Warn, TestLogDirection.Local,
                settings.DeviceId, remoteDeviceId: originDeviceId, detail: "Ursprungsgeraet unbekannt, wartet auf Discovery");
            return; // will be retried on the next announce/boot-call once we've learned this device
        }

        // Admin-Rollen-Kryptoverifikation (Nutzerwunsch 17.08.2026): eine als Config-
        // Ursprung akzeptierte Herkunft muss ein tatsächlich verifiziertes Admin-Gerät
        // sein, sonst könnte jedes Gerät der Kundengruppe eine erfundene, höhere
        // ConfigVersion behaupten und die eigene (manipulierte) Config als "neuer"
        // andrehen. Sichtbar statt still verworfen, damit ein Admin eine echte
        // Migrationslücke (Bestandsgerät ohne Admin-Rollen-Schlüssel) auch bemerkt.
        if (originDevice.Role != Role.Admin || !originDevice.AdminVerified)
        {
            _audit?.Invoke($"configsync: announce von nicht verifiziertem absender={originDeviceId} ignoriert");
            TestLogger.LogAction(TestLogEventType.ActionSkipped, TestLogLevel.Warn, TestLogDirection.Local,
                settings.DeviceId, remoteDeviceId: originDeviceId, detail: "Ursprung nicht Admin/nicht verifiziert");
            return;
        }

        await _applyLock.WaitAsync(ct);
        try
        {
            // Re-check under the lock in case a concurrent pull already applied this or a newer version.
            settings = _settingsStore.Load();
            if (remoteConfigVersion <= settings.AppliedConfigVersion)
            {
                return;
            }

            var pulled = await PullFromAsync(originDevice, _identityProvider(), ct);
            if (pulled is not null && pulled.ConfigVersion > settings.AppliedConfigVersion)
            {
                _configStore.Save(pulled);
                ApplyToSelf(pulled);
                _audit?.Invoke($"configsync applied version={pulled.ConfigVersion} from device={originDeviceId}");
            }
        }
        finally
        {
            _applyLock.Release();
        }
    }

    private async Task<SharedConfig?> PullFromAsync(DeviceEntry originDevice, LiveIdentity identity, CancellationToken ct)
    {
        try
        {
            if (!IPAddress.TryParse(originDevice.IpAddress, out var address))
            {
                return null;
            }

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            await client.ConnectAsync(address, AppConstants.ConfigSyncTcpPort, timeoutCts.Token);
            await using var stream = client.GetStream();

            var request = new ConfigSyncPullRequestMessage(identity.CustomerGroupId, identity.DeviceId);

            // LAN-Verschlüsselung (CLAUDE.md "Lizenz & Secrets"): gleiche Fallback-Logik
            // wie AlarmSender - nur wenn wir einen Gruppenschlüssel haben UND das Ziel als
            // verschlüsselungsfähig+gepinnt bekannt ist. Kein Zustellzwang wie beim Alarm-
            // Kanal nötig: schlägt der Pull fehl, wird er beim nächsten Announce/Boot-Call
            // ohnehin erneut versucht (EvaluateAndPullAsync-Klassendoku).
            var groupKeyBase64 = _groupKeyProvider?.Invoke();
            var canEncrypt = groupKeyBase64 is not null
                && originDevice.ProtocolVersion is >= AppConstants.CurrentProtocolVersion
                && !string.IsNullOrEmpty(originDevice.PinnedDeviceIdentityPublicKeyBase64);

            string requestLine;
            if (canEncrypt)
            {
                var devicePrivateKeyBase64 = DeviceIdentityStore.LoadOrCreate().PrivateKeyBase64;
                var envelope = SecureEnvelopeCodec.Seal(request, request.CustomerGroupId, request.RequesterDeviceId, groupKeyBase64, devicePrivateKeyBase64, DateTimeOffset.UtcNow);
                requestLine = envelope is not null ? NetworkSerializer.ToJsonLine(envelope) : NetworkSerializer.ToJsonLine(request);
            }
            else
            {
                requestLine = NetworkSerializer.ToJsonLine(request);
            }

            var payload = NetworkSerializer.Encoding.GetBytes(requestLine);
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);
            TestLogger.LogAction(TestLogEventType.MessageSent, TestLogLevel.Info, TestLogDirection.Send,
                identity.DeviceId, remoteDeviceId: originDevice.DeviceId, detail: "ConfigSyncPullRequest");

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return null;
            }

            // Antwortformat spiegelt das Anfrageformat (siehe AlarmTcpListener-Klassendoku
            // für dasselbe Prinzip).
            SharedConfig? config;
            if (SecureEnvelopeCodec.TryParse(line, out var responseEnvelope) && responseEnvelope is not null)
            {
                var response = SecureEnvelopeCodec.TryOpen<ConfigSyncPullResponseMessage>(responseEnvelope, groupKeyBase64, originDevice.PinnedDeviceIdentityPublicKeyBase64, DateTimeOffset.UtcNow);
                config = response?.Config;
            }
            else
            {
                var response = NetworkSerializer.FromJsonLine<ConfigSyncPullResponseMessage>(line);
                config = response?.Config;
            }

            if (config is not null)
            {
                TestLogger.LogAction(TestLogEventType.MessageReceived, TestLogLevel.Info, TestLogDirection.Receive,
                    identity.DeviceId, remoteDeviceId: originDevice.DeviceId, detail: "ConfigSyncPullResponse");
            }

            return config;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task TcpAcceptLoopAsync(TcpListener listener, CancellationToken ct)
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

            _ = HandlePullRequestAsync(client, ct);
        }
    }

    private async Task HandlePullRequestAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 10000;
            await using var stream = client.GetStream();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            var identity = _identityProvider();

            ConfigSyncPullRequestMessage? request;
            var wasEncrypted = false;
            string? requesterPinnedKey = null;

            if (SecureEnvelopeCodec.TryParse(line, out var envelope) && envelope is not null)
            {
                if (!CustomerGroupFilter.Matches(envelope.CustomerGroupId, identity.CustomerGroupId) || !envelope.IsPlausible())
                {
                    return;
                }

                requesterPinnedKey = _deviceListProvider().FirstOrDefault(d => d.DeviceId == envelope.DeviceId)?.PinnedDeviceIdentityPublicKeyBase64;
                var groupKeyBase64 = _groupKeyProvider?.Invoke();
                request = SecureEnvelopeCodec.TryOpen<ConfigSyncPullRequestMessage>(envelope, groupKeyBase64, requesterPinnedKey, DateTimeOffset.UtcNow);
                if (request is null)
                {
                    return; // falscher Gruppenschlüssel/manipuliert/falscher Absender-Schlüssel - stiller Drop
                }

                wasEncrypted = true;
            }
            else
            {
                request = NetworkSerializer.FromJsonLine<ConfigSyncPullRequestMessage>(line);
                if (request is null || request.RequesterDeviceId == Guid.Empty)
                {
                    return;
                }

                if (!CustomerGroupFilter.Matches(request.CustomerGroupId, identity.CustomerGroupId))
                {
                    return;
                }
            }

            TestLogger.LogAction(TestLogEventType.MessageReceived, TestLogLevel.Info, TestLogDirection.Receive,
                identity.DeviceId, remoteDeviceId: request.RequesterDeviceId, detail: "ConfigSyncPullRequest");

            var config = _configStore.LoadOrCreate();
            var response = new ConfigSyncPullResponseMessage(identity.CustomerGroupId, config);

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
            TestLogger.LogAction(TestLogEventType.MessageSent, TestLogLevel.Info, TestLogDirection.Send,
                identity.DeviceId, remoteDeviceId: request.RequesterDeviceId, detail: "ConfigSyncPullResponse");
        }
        catch (Exception)
        {
            // best-effort - the requester just times out and retries on the next announce
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _udpSocket?.Dispose();
        _tcpListener?.Stop();
        if (_udpReceiveLoop is not null)
        {
            try { await _udpReceiveLoop; } catch { /* handled inside loop */ }
        }
        if (_tcpAcceptLoop is not null)
        {
            try { await _tcpAcceptLoop; } catch { /* handled inside loop */ }
        }
        _cts?.Dispose();
        _applyLock.Dispose();
    }
}
