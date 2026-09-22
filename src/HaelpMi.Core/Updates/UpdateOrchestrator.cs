using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Ipc;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Updates;

/// <summary>
/// DEPRECATED für release-1.0-MVP: wird zur Laufzeit nirgends instanziiert/gestartet
/// (siehe HaelpMi.Agent/App.xaml.cs) - Update dieses Release-Zweigs läuft ausschließlich
/// manuell (neuer Installer-Lauf pro Gerät).
///
/// Orchestriert die 0-Downtime-Update-Pipeline (Abschnitt 11) auf einem Gerät: hört auf
/// <see cref="DiscoveryService.PeerVersionObserved"/> (bereits vorhandener Boot-Call-
/// Mechanismus), entscheidet ob/wann dieses Gerät dran ist (Admin-Freigabe + gestaffelte
/// Kreis-Quote + Kill-Switch-Sperre + zufälliger Jitter), zieht das Paket per P2P, lässt
/// den privilegierten HaelpMi.UpdateService installieren/testen/swappen und wertet den
/// eigenen Selbsttest sowie eine einfache Peer-Erreichbarkeits-Bestätigung aus, bevor der
/// eigentliche Swap freigegeben wird.
///
/// Vereinfachungen ggü. einem vollständigen Ausbau (bewusst, siehe CLAUDE.md "kein
/// Over-Engineering" - hier nicht weiter spezifiziert):
/// - "Peer-Bestätigung" ist ein reiner TCP-Erreichbarkeits-Handshake zu einem beliebigen
///   bekannten Gerät (Alarm-Port), kein vollständiger "Peer führt denselben Testlauf
///   selbst nochmal aus"-Runde.
/// - Die Kreis-Quote bestimmt "wer ist dran" über eine stabile Sortierung der Geräte-IDs
///   im Kreis, nicht über eine vom Admin einzeln kuratierte Geräteliste.
/// </summary>
public sealed class UpdateOrchestrator
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Func<List<DeviceEntry>> _deviceListProvider;
    private readonly Func<SharedConfig> _sharedConfigProvider;
    private readonly UpdatePackageDistributionService _distribution;
    private readonly UpdatePackageCacheStore _cacheStore;
    private readonly UpdateServiceIpcClient _updateServiceClient;
    private readonly UpdateLockoutStore _lockoutStore;
    private readonly Action<string>? _audit;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UpdateOrchestrator(
        Func<LiveIdentity> identityProvider,
        Func<List<DeviceEntry>> deviceListProvider,
        Func<SharedConfig> sharedConfigProvider,
        UpdatePackageDistributionService distribution,
        UpdatePackageCacheStore cacheStore,
        UpdateServiceIpcClient updateServiceClient,
        UpdateLockoutStore lockoutStore,
        Action<string>? audit = null)
    {
        _identityProvider = identityProvider;
        _deviceListProvider = deviceListProvider;
        _sharedConfigProvider = sharedConfigProvider;
        _distribution = distribution;
        _cacheStore = cacheStore;
        _updateServiceClient = updateServiceClient;
        _lockoutStore = lockoutStore;
        _audit = audit;
    }

    /// <summary>An <see cref="DiscoveryService.PeerVersionObserved"/> hängen.</summary>
    public void OnPeerVersionObserved(object? sender, PeerVersionInfo info) => _ = TryHandlePeerVersionAsync(info);

    private async Task TryHandlePeerVersionAsync(PeerVersionInfo info)
    {
        if (!await _gate.WaitAsync(0))
        {
            return; // schon ein Update-Versuch im Gange - keine zwei parallel
        }

        try
        {
            var identity = _identityProvider();
            if (!IsNewer(info.ProgramVersion, identity.ProgramVersion))
            {
                return;
            }

            var lockout = _lockoutStore.Load();
            if (lockout.LockedUntilUtc is { } lockedUntil && DateTimeOffset.UtcNow < lockedUntil)
            {
                _audit?.Invoke($"update skipped (kill-switch aktiv bis {lockedUntil:o}) version={info.ProgramVersion}");
                return;
            }

            var config = _sharedConfigProvider();
            if (config.UpdateRollout.ApprovedVersion != info.ProgramVersion)
            {
                return; // Admin hat genau diese Version (noch) nicht freigegeben
            }

            var devices = _deviceListProvider();
            if (!IsMyTurn(identity, config, devices))
            {
                return; // Kreis-Quote für diese Version noch nicht erreicht
            }

            var jitterSeconds = Random.Shared.Next(AppConstants.UpdatePullJitter.MinSeconds, AppConstants.UpdatePullJitter.MaxSeconds + 1);
            await Task.Delay(TimeSpan.FromSeconds(jitterSeconds));

            var succeeded = await AttemptUpdateAsync(info, devices);
            if (succeeded)
            {
                _lockoutStore.ResetFailures();
            }
            else
            {
                var failures = _lockoutStore.RecordFailure();
                if (failures >= AppConstants.UpdateMaxConsecutiveFailures)
                {
                    var until = DateTimeOffset.UtcNow + AppConstants.UpdateLockoutDuration;
                    _lockoutStore.SetLockout(until);
                    _audit?.Invoke($"update kill-switch ausgelöst nach {failures} Fehlschlägen - gesperrt bis {until:o}");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static bool IsNewer(string candidate, string current)
    {
        // Programmversionen sind "Major.Minor.Patch" (siehe MyAppVersion in den Installer-
        // Skripten) - numerischer Vergleich wie CompareDottedVersion dort, keine simple
        // String-Ordnung (die "1.10.0" fälschlich vor "1.2.0" einsortieren würde).
        var candidateParts = candidate.Split('.');
        var currentParts = current.Split('.');
        var length = Math.Max(candidateParts.Length, currentParts.Length);
        for (var i = 0; i < length; i++)
        {
            var c = i < candidateParts.Length && int.TryParse(candidateParts[i], out var cv) ? cv : 0;
            var k = i < currentParts.Length && int.TryParse(currentParts[i], out var kv) ? kv : 0;
            if (c != k)
            {
                return c > k;
            }
        }
        return false;
    }

    // War früher pro Kreis gestaffelt (Dictionary&lt;CircleId, Quota&gt;) - mit dem Wegfall
    // des Kreis-Konzepts (04.08.2026, siehe EditScope.cs) auf eine einzige kundengruppen-
    // weite Quote vereinfacht: die ersten N Geräte (stabile Sortierung nach Geräte-ID)
    // dürfen aktualisieren.
    internal static bool IsMyTurn(LiveIdentity identity, SharedConfig config, IReadOnlyList<DeviceEntry> devices)
    {
        var quota = config.UpdateRollout.ApprovedDeviceQuota;
        if (quota <= 0)
        {
            return false;
        }

        var allIds = devices.Select(d => d.DeviceId).ToHashSet();
        allIds.Add(identity.DeviceId); // die eigene Geräteliste kennt das eigene Gerät nicht als "anderes Gerät"

        var ordered = allIds.OrderBy(id => id).ToList();
        var myIndex = ordered.IndexOf(identity.DeviceId);
        return myIndex >= 0 && myIndex < quota;
    }

    private async Task<bool> AttemptUpdateAsync(PeerVersionInfo info, IReadOnlyList<DeviceEntry> devices)
    {
        var sourcePeer = devices.FirstOrDefault(d => d.DeviceId == info.DeviceId);
        if (sourcePeer is null)
        {
            return false;
        }

        var pulled = await _distribution.PullFromAsync(sourcePeer, info.ProgramVersion);
        if (pulled is null)
        {
            _audit?.Invoke($"update pull fehlgeschlagen version={info.ProgramVersion} peer={info.DeviceId}");
            return false;
        }

        var (manifest, payload) = pulled.Value;
        _cacheStore.Save(info.ProgramVersion, manifest, payload); // ermöglicht Weiterverteilung an andere Peers, sobald wir selbst auf dieser Version laufen
        var packagePath = Path.Combine(UpdatePackageCacheStore.DirectoryFor(info.ProgramVersion), "package.zip");

        var install = await _updateServiceClient.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.Install, info.ProgramVersion, PackageZipPath: packagePath));
        if (!install.Success)
        {
            _audit?.Invoke($"update install fehlgeschlagen: {install.Error}");
            return false;
        }

        var testPort = GetEphemeralPort();
        var startTest = await _updateServiceClient.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.StartTest, info.ProgramVersion, TestPort: testPort));
        if (!startTest.Success)
        {
            _audit?.Invoke($"update start-test fehlgeschlagen: {startTest.Error}");
            await RollbackAsync(info.ProgramVersion);
            return false;
        }

        if (!await PingTestInstanceAsync(testPort))
        {
            _audit?.Invoke("update lokaler Selbsttest fehlgeschlagen");
            await RollbackAsync(info.ProgramVersion);
            return false;
        }

        if (!await HasReachablePeerAsync(devices, info.DeviceId))
        {
            _audit?.Invoke("update Peer-Bestätigung fehlgeschlagen - kein Peer erreichbar");
            await RollbackAsync(info.ProgramVersion);
            return false;
        }

        var swap = await _updateServiceClient.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.ConfirmSwap, info.ProgramVersion), TimeSpan.FromMinutes(2));
        if (!swap.Success)
        {
            _audit?.Invoke($"update swap fehlgeschlagen: {swap.Error}");
            return false; // kein automatisches Rollback mehr an dieser Stelle möglich, siehe UpdateServiceWorker.ConfirmSwapAsync
        }

        _audit?.Invoke($"update erfolgreich, läuft jetzt auf version={info.ProgramVersion}");
        return true;
    }

    private Task RollbackAsync(string version) =>
        _updateServiceClient.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.Rollback, version));

    private static int GetEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Verbindet sich zum lokalen Selbsttest-Port der Testinstanz (siehe HaelpMi.Agent
    // App.xaml.cs, --update-test-port). Mehrere Versuche mit kurzer Pause, weil der neu
    // gestartete Prozess einen Moment braucht, bis sein Listener steht.
    private static async Task<bool> PingTestInstanceAsync(int testPort)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(35);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(IPAddress.Loopback, testPort, connectCts.Token);

                await using var stream = client.GetStream();
                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var buffer = new byte[8];
                var read = await stream.ReadAsync(buffer, readCts.Token);
                if (read > 0 && System.Text.Encoding.UTF8.GetString(buffer, 0, read).TrimEnd() == "OK")
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Testinstanz noch nicht bereit oder abgestürzt - kurz warten und erneut versuchen
            }

            await Task.Delay(500);
        }

        return false;
    }

    // "Mindestens eine Peer-Bestätigung" (CLAUDE.md) - hier bewusst ein einfacher
    // Erreichbarkeits-Handshake (reiner TCP-Connect zum Alarm-Port, ohne Daten zu senden)
    // statt eines vollständigen "Peer testet das Paket selbst nochmal"-Protokolls: bestätigt
    // zumindest, dass gerade nicht das ganze lokale Netz down ist, bevor wir swappen.
    private static async Task<bool> HasReachablePeerAsync(IReadOnlyList<DeviceEntry> devices, Guid excludeDeviceId)
    {
        foreach (var device in devices.Where(d => d.DeviceId != excludeDeviceId))
        {
            if (!IPAddress.TryParse(device.IpAddress, out var address))
            {
                continue;
            }

            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await client.ConnectAsync(address, AppConstants.AlarmTcpPort, cts.Token);
                return true;
            }
            catch (Exception)
            {
                // nächsten Peer versuchen
            }
        }

        return false;
    }
}
