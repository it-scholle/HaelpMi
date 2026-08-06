using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;
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
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    private UdpClient? _udpSocket;
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private Task? _udpReceiveLoop;
    private Task? _tcpAcceptLoop;

    /// <summary>Raised after a newer config has been pulled and applied - callers should re-register hotkeys, refresh UI, etc.</summary>
    public event EventHandler<SharedConfig>? ConfigApplied;

    public ConfigSyncService(Func<LiveIdentity> identityProvider, Func<List<DeviceEntry>> deviceListProvider, Action<string>? audit = null)
    {
        _identityProvider = identityProvider;
        _deviceListProvider = deviceListProvider;
        _audit = audit;
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

        var settings = _settingsStore.Load();
        if (announce.ConfigVersion <= settings.AppliedConfigVersion)
        {
            return; // already current or stale announce - nothing to do
        }

        var originDevice = _deviceListProvider().FirstOrDefault(d => d.DeviceId == announce.OriginDeviceId);
        if (originDevice is null)
        {
            _audit?.Invoke($"configsync announce from unknown device={announce.OriginDeviceId} - awaiting discovery");
            return; // will be retried on the next announce once we've learned this device via boot-call
        }

        await _applyLock.WaitAsync(ct);
        try
        {
            // Re-check under the lock in case a concurrent pull already applied this or a newer version.
            settings = _settingsStore.Load();
            if (announce.ConfigVersion <= settings.AppliedConfigVersion)
            {
                return;
            }

            var pulled = await PullFromAsync(originDevice, identity, ct);
            if (pulled is not null && pulled.ConfigVersion > settings.AppliedConfigVersion)
            {
                _configStore.Save(pulled);
                ApplyToSelf(pulled);
                _audit?.Invoke($"configsync applied version={pulled.ConfigVersion} from device={announce.OriginDeviceId}");
            }
        }
        finally
        {
            _applyLock.Release();
        }
    }

    private static async Task<SharedConfig?> PullFromAsync(DeviceEntry originDevice, LiveIdentity identity, CancellationToken ct)
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
            var payload = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(request));
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            if (line is null)
            {
                return null;
            }

            var response = NetworkSerializer.FromJsonLine<ConfigSyncPullResponseMessage>(line);
            return response?.Config;
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

            var request = NetworkSerializer.FromJsonLine<ConfigSyncPullRequestMessage>(line);
            if (request is null || request.RequesterDeviceId == Guid.Empty)
            {
                return;
            }

            var identity = _identityProvider();
            if (!CustomerGroupFilter.Matches(request.CustomerGroupId, identity.CustomerGroupId))
            {
                return;
            }

            var config = _configStore.LoadOrCreate();
            var response = new ConfigSyncPullResponseMessage(identity.CustomerGroupId, config);
            var responseBytes = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(response));
            await stream.WriteAsync(responseBytes, ct);
            await stream.FlushAsync(ct);
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
