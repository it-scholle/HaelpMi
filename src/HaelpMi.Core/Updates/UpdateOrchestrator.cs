using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Ipc;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Updates;

/// <summary>
/// Orchestriert die 0-Downtime-Update-Pipeline (Abschnitt 11) auf einem Gerät: hört auf
/// <see cref="DiscoveryService.PeerVersionObserved"/> (bereits vorhandener Boot-Call-
/// Mechanismus), entscheidet ob dieses Gerät aktualisieren darf (Admin-Freigabe genau
/// dieser Version + Wellen-Gate + Kill-Switch-Sperre + zufälliger Jitter - siehe CLAUDE.md
/// "Rollout-Freigabe"), zieht das Paket per P2P, lässt den privilegierten
/// HaelpMi.UpdateService installieren/testen/swappen und wertet den eigenen Selbsttest
/// sowie eine einfache Peer-Erreichbarkeits-Bestätigung aus, bevor der eigentliche Swap
/// freigegeben wird.
///
/// Wellen-Rollout (korrigiert 16.08.2026, siehe <see cref="IsMyTurn"/>): kein manuell
/// gestuftes Freigabekontingent mehr wie vor v0.18.0 (Admin musste damals jede Stufe
/// einzeln freigeben) - stattdessen ergibt sich die erlaubte Wellenbreite automatisch aus
/// der Zahl bereits aktualisierter Peers, kein Admin-Klick pro Stufe nötig.
///
/// Vereinfachung ggü. einem vollständigen Ausbau (bewusst, siehe CLAUDE.md "kein
/// Over-Engineering" - hier nicht weiter spezifiziert): "Peer-Bestätigung" ist ein reiner
/// TCP-Erreichbarkeits-Handshake zu einem beliebigen bekannten Gerät (Alarm-Port), kein
/// vollständiger "Peer führt denselben Testlauf selbst nochmal aus"-Runde.
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
                return; // Wellen-Kontingent für diese Version noch nicht erreicht
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

    internal static bool IsNewer(string candidate, string current) => CompareVersions(candidate, current) > 0;

    // Programmversionen sind "Major.Minor.Patch" (siehe MyAppVersion in den Installer-
    // Skripten) - numerischer Vergleich wie CompareDottedVersion dort, keine simple
    // String-Ordnung (die "1.10.0" fälschlich vor "1.2.0" einsortieren würde). Fehlende/nicht
    // parsbare Segmente (z. B. ein noch nie beobachtetes DeviceEntry.LastKnownProgramVersion
    // == "") zählen als 0, sind also immer "älter" als jede echte Versionsnummer.
    private static int CompareVersions(string a, string b)
    {
        var aParts = a.Split('.');
        var bParts = b.Split('.');
        var length = Math.Max(aParts.Length, bParts.Length);
        for (var i = 0; i < length; i++)
        {
            var av = i < aParts.Length && int.TryParse(aParts[i], out var an) ? an : 0;
            var bv = i < bParts.Length && int.TryParse(bParts[i], out var bn) ? bn : 0;
            if (av != bv)
            {
                return av.CompareTo(bv);
            }
        }
        return 0;
    }

    /// <summary>
    /// Wellen-Gate (CLAUDE.md, Rollout-Freigabe korrigiert 16.08.2026): ersetzt das bis
    /// v0.17.x manuell gestufte Freigabekontingent (<c>SharedConfig.UpdateRolloutState.
    /// ApprovedDeviceQuota</c>, in v0.18.0 komplett entfernt) durch eine automatisch
    /// abgeleitete Wellenbreite n = Zahl der Peers, die laut eigenem, zwangsläufig
    /// unvollständigem Geräte-Cache (<see cref="DeviceEntry.LastKnownProgramVersion"/>, aus
    /// Boot-Call + KnownDeviceSummary-Gossip) die freigegebene Version schon erfolgreich
    /// übernommen haben. Kein Admin-Klick pro Stufe, kein zentraler Zähler - bleibt P2P,
    /// jedes Gerät schätzt n rein aus seiner eigenen Sicht. Gleiche deterministische
    /// Reihenfolge wie die frühere Implementierung (stabile DeviceId-Sortierung), nur mit
    /// dynamischem statt admin-gesetztem n.
    ///
    /// n=0 (noch kein einziger bekannter Peer auf der freigegebenen Version) blockiert
    /// bewusst jeden Peer-Beobachtungs-getriebenen Versuch - das allererste Gerät jeder
    /// Kundengruppe kommt nicht über dieses Gate auf eine neue Version, sondern über das
    /// separate, vom Menschen einmalig angestoßene HaelpMi.UpdateBootstrapper-Tool (siehe
    /// dortiger Klassenkommentar), das komplett außerhalb dieser Peer-Beobachtungskette
    /// läuft. Erst danach kennt überhaupt ein Peer die neue Version, und n startet bei 1.
    /// </summary>
    internal static bool IsMyTurn(LiveIdentity identity, SharedConfig config, IReadOnlyList<DeviceEntry> devices)
    {
        var approvedVersion = config.UpdateRollout.ApprovedVersion;
        if (string.IsNullOrWhiteSpace(approvedVersion))
        {
            return false;
        }

        var updatedCount = devices.Count(d => CompareVersions(d.LastKnownProgramVersion, approvedVersion) >= 0);
        if (updatedCount <= 0)
        {
            return false;
        }

        var allIds = devices.Select(d => d.DeviceId).ToHashSet();
        allIds.Add(identity.DeviceId); // die eigene Geräteliste kennt das eigene Gerät nicht als "anderes Gerät"

        var ordered = allIds.OrderBy(id => id).ToList();
        var myIndex = ordered.IndexOf(identity.DeviceId);
        return myIndex >= 0 && myIndex < updatedCount;
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

        var testPort = UpdateTestInstancePing.GetEphemeralPort();
        var startTest = await _updateServiceClient.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.StartTest, info.ProgramVersion, TestPort: testPort));
        if (!startTest.Success)
        {
            _audit?.Invoke($"update start-test fehlgeschlagen: {startTest.Error}");
            await RollbackAsync(info.ProgramVersion);
            return false;
        }

        if (!await UpdateTestInstancePing.PingAsync(testPort))
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

        // Flaw 26 (v0.39.x): eigene PID mitschicken, damit UpdateServiceWorker gezielt DIESEN
        // Prozess beendet (und dessen tatsächliches Ende abwartet), statt blind nach Namen zu
        // killen - siehe UpdateServiceRequest.CallerProcessId-Klassendoku.
        var swap = await _updateServiceClient.SendAsync(
            new UpdateServiceRequest(UpdateServiceCommandType.ConfirmSwap, info.ProgramVersion, CallerProcessId: Environment.ProcessId),
            TimeSpan.FromMinutes(2));
        if (!swap.Success)
        {
            _audit?.Invoke($"update swap fehlgeschlagen: {swap.Error}");
            // Anders als der vorherige Kommentar hier noch behauptete: UpdateServiceWorker
            // rollt einen gescheiterten Swap seit Flaw 26 selbst zurück (Datei-Rückschub +
            // Neustart der alten Version) - dieser Rückgabewert bedeutet nur noch "kein
            // Erfolg", nicht mehr zwangsläufig "Gerät jetzt in undefiniertem Zustand".
            return false;
        }

        _audit?.Invoke($"update erfolgreich, läuft jetzt auf version={info.ProgramVersion}");
        return true;
    }

    private Task RollbackAsync(string version) =>
        _updateServiceClient.SendAsync(new UpdateServiceRequest(UpdateServiceCommandType.Rollback, version));

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
