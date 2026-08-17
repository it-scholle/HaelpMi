using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Networking;

public enum EditLockAcquireOutcome
{
    Granted,
    DeniedByHolder,
    CollisionRetryExhausted,
}

public sealed class EditLockAcquireResult
{
    public required EditLockAcquireOutcome Outcome { get; init; }
    public string? HolderComputerName { get; init; }
    public string? HolderUser { get; init; }
}

/// <summary>
/// Exclusive per-record edit lock (Teil 2, Abschnitt 5): a TCP "will editieren"-Call
/// sent directly to every admin-capable device in the same Kunden-Gruppe, scoped to
/// exactly the Gruppe or Alarm-Profil being edited (<see cref="EditScopeKind"/> + Guid) -
/// war früher pro Kreis, siehe EditScope.cs für den Hintergrund. Both the requesting and
/// the answering side live in one class, same pattern as <see cref="DiscoveryService"/>,
/// because every Admin device is simultaneously both - it answers other admins'
/// requests while possibly making its own. Several different scopes can be held at once
/// (e.g. one Gruppe and one Alarm-Profil simultaneously selected in two Dashboard-Tabs).
///
/// Collision handling (Abschnitt 5): while a <see cref="TryAcquireAsync"/> call for a
/// scope is in flight, an *inbound* request for that same scope is treated as a
/// simultaneous collision - both sides deny each other and release immediately, then
/// each retries independently after its own random backoff
/// (<see cref="AppConstants.EditLockCollisionBackoff"/>). A collision is deliberately
/// distinct from "someone else already holds the lock" (<see cref="EditLockAcquireOutcome.DeniedByHolder"/>),
/// which does not auto-retry - that is a real, not a racy, conflict.
/// </summary>
public sealed class EditLockService : IAsyncDisposable
{
    private const int MaxCollisionRetries = 5;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    private sealed class ScopeLockState
    {
        public bool IsHeldByMe;
        public bool IsAcquiring;
        public bool CollisionDetectedDuringAcquire;
        public System.Threading.Timer? InactivityTimer;
    }

    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Action<string>? _audit;
    private readonly Func<List<DeviceEntry>>? _deviceListProvider;
    private readonly ConcurrentDictionary<(EditScopeKind Kind, Guid Id), ScopeLockState> _states = new();
    private readonly Random _random = Random.Shared;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    /// <param name="deviceListProvider">
    /// Admin-Rollen-Kryptoverifikation (Nutzerwunsch 17.08.2026): wenn gesetzt, antwortet
    /// <see cref="HandleIncomingRequestAsync"/> einem Requester nur, wenn er in dieser
    /// Liste als <see cref="Role.Admin"/> UND <see cref="DeviceEntry.AdminVerified"/>
    /// bekannt ist - nur Verteidigung in der Tiefe (eine ausbleibende Antwort gilt laut
    /// Abschnitt 5 ohnehin als "Zugriff gewährt", ein von einem nicht verifizierten Gerät
    /// erhaltenes "Granted" beeinflusst nirgends den lokalen Zustand eines echten Admins).
    /// Bewusst optional/null-tolerant (alte Semantik ohne Prüfung) statt Pflicht wie bei
    /// AuditSyncService, weil hier kein eigenständiger Sicherheitsgewinn dranhängt.
    /// </param>
    public EditLockService(Func<LiveIdentity> identityProvider, Action<string>? audit = null, Func<List<DeviceEntry>>? deviceListProvider = null)
    {
        _identityProvider = identityProvider;
        _audit = audit;
        _deviceListProvider = deviceListProvider;
    }

    public void Start(int port = AppConstants.EditLockTcpPort)
    {
        if (_listener is not null)
        {
            return;
        }

        // Entdeckt 05.08.2026 ("Dashboard startet nicht" - derselbe Symptomname, der schon
        // einmal zum ConfigSyncService-Fix führte, hier aber nie nachgezogen): ein
        // SocketException hier (Port schon belegt, z. B. ein noch nicht vollständig
        // beendeter Config.exe-Prozess während/kurz nach einem Update) riss bisher
        // ungefangen durch OpenAdminDashboardAsync - das Fenster wurde nie angezeigt, ohne
        // jede Fehlermeldung. Gleiches Graceful-Fallback-Muster wie ConfigSyncService.Start():
        // eingehende "will editieren"-Anfragen werden dann von dem Prozess beantwortet, der
        // den Port bereits hält; TryAcquireAsync/RequestOneAsync senden ohnehin über eigene
        // ausgehende Verbindungen, unabhängig von _listener - nur das EMPFANGEN von
        // Lock-Anfragen auf diesem Prozess fällt in diesem Fall aus, nicht das ganze Dashboard.
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _acceptLoop = AcceptLoopAsync(_listener, _cts.Token);
        }
        catch (SocketException)
        {
            _listener = null;
        }
    }

    /// <summary>Called when the Admin selects a Gruppe/Alarm-Profil to edit. See class remarks for collision handling.</summary>
    public async Task<EditLockAcquireResult> TryAcquireAsync(EditScopeKind scopeKind, Guid scopeId, IReadOnlyList<DeviceEntry> adminPeers, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt <= MaxCollisionRetries; attempt++)
        {
            var result = await TryAcquireOnceAsync(scopeKind, scopeId, adminPeers, ct);
            if (result is not null)
            {
                return result;
            }

            // Collision: both sides back off randomly and retry (Abschnitt 5).
            var backoffMs = _random.Next(AppConstants.EditLockCollisionBackoff.MinMs, AppConstants.EditLockCollisionBackoff.MaxMs + 1);
            _audit?.Invoke($"editlock collision scope={scopeKind}:{scopeId} attempt={attempt} backoffMs={backoffMs}");
            await Task.Delay(backoffMs, ct);
        }

        return new EditLockAcquireResult { Outcome = EditLockAcquireOutcome.CollisionRetryExhausted };
    }

    /// <summary>Returns null specifically to signal "collision, caller should back off and retry".</summary>
    private async Task<EditLockAcquireResult?> TryAcquireOnceAsync(EditScopeKind scopeKind, Guid scopeId, IReadOnlyList<DeviceEntry> adminPeers, CancellationToken ct)
    {
        var key = (scopeKind, scopeId);
        var state = _states.GetOrAdd(key, _ => new ScopeLockState());
        state.IsAcquiring = true;
        state.CollisionDetectedDuringAcquire = false;

        try
        {
            var identity = _identityProvider();
            var request = new EditLockRequestMessage(identity.CustomerGroupId, scopeKind, scopeId, identity.DeviceId, identity.ComputerName, identity.User, DateTimeOffset.UtcNow);

            var responses = await Task.WhenAll(adminPeers.Select(peer => RequestOneAsync(peer, request, ct)));

            if (state.CollisionDetectedDuringAcquire)
            {
                return null; // signal caller to back off and retry
            }

            var denial = responses.FirstOrDefault(r => r is { Granted: false });
            if (denial is not null)
            {
                return new EditLockAcquireResult
                {
                    Outcome = EditLockAcquireOutcome.DeniedByHolder,
                    HolderComputerName = denial.HolderComputerName,
                    HolderUser = denial.HolderUser,
                };
            }

            // Every reachable peer granted (or none were reachable/known - "Antwortet
            // niemand -> Zugriff gewährt", Abschnitt 5).
            state.IsHeldByMe = true;
            ResetInactivityTimer(key, state);
            _audit?.Invoke($"editlock acquired scope={scopeKind}:{scopeId}");
            return new EditLockAcquireResult { Outcome = EditLockAcquireOutcome.Granted };
        }
        finally
        {
            state.IsAcquiring = false;
        }
    }

    private static async Task<EditLockResponseMessage?> RequestOneAsync(DeviceEntry peer, EditLockRequestMessage request, CancellationToken ct)
    {
        try
        {
            if (!IPAddress.TryParse(peer.IpAddress, out var address))
            {
                return null;
            }

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(RequestTimeout);

            await client.ConnectAsync(address, AppConstants.EditLockTcpPort, timeoutCts.Token);
            await using var stream = client.GetStream();

            var payload = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(request));
            await stream.WriteAsync(payload, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            var line = await BoundedLineReader.ReadLineAsync(stream, timeoutCts.Token);
            return line is null ? null : NetworkSerializer.FromJsonLine<EditLockResponseMessage>(line);
        }
        catch (Exception)
        {
            return null; // unreachable peer counts as "no objection" (Abschnitt 5: "Antwortet niemand -> Zugriff gewährt")
        }
    }

    /// <summary>Call on every dashboard edit action to keep the 10-minute inactivity auto-release from firing.</summary>
    public void TouchActivity(EditScopeKind scopeKind, Guid scopeId)
    {
        var key = (scopeKind, scopeId);
        if (_states.TryGetValue(key, out var state) && state.IsHeldByMe)
        {
            ResetInactivityTimer(key, state);
        }
    }

    /// <summary>Explicit release when the admin deselects/leaves the record or closes the dashboard.</summary>
    public void Release(EditScopeKind scopeKind, Guid scopeId)
    {
        var key = (scopeKind, scopeId);
        if (_states.TryGetValue(key, out var state))
        {
            state.IsHeldByMe = false;
            state.InactivityTimer?.Dispose();
            state.InactivityTimer = null;
        }

        _audit?.Invoke($"editlock released scope={scopeKind}:{scopeId}");
    }

    private void ResetInactivityTimer((EditScopeKind Kind, Guid Id) key, ScopeLockState state)
    {
        state.InactivityTimer?.Dispose();
        state.InactivityTimer = new System.Threading.Timer(_ => Release(key.Kind, key.Id), null, AppConstants.EditLockInactivityTimeout, Timeout.InfiniteTimeSpan);
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

            EditLockRequestMessage? request;
            try
            {
                request = NetworkSerializer.FromJsonLine<EditLockRequestMessage>(line);
            }
            catch (Exception)
            {
                return;
            }

            if (request is null || request.RequesterDeviceId == Guid.Empty || request.ScopeId == Guid.Empty)
            {
                return;
            }

            var identity = _identityProvider();
            if (!CustomerGroupFilter.Matches(request.CustomerGroupId, identity.CustomerGroupId))
            {
                return; // Teil 2, Abschnitt 6
            }

            // Admin-Rollen-Kryptoverifikation (Nutzerwunsch 17.08.2026) - siehe
            // Konstruktor-Kommentar zu deviceListProvider für Hintergrund/Tragweite. Nur
            // aktiv, wenn ein Provider gesetzt ist (Produktivpfad); ohne Provider
            // unverändertes altes Verhalten.
            if (_deviceListProvider is not null)
            {
                var requesterIsVerifiedAdmin = _deviceListProvider()
                    .Any(d => d.DeviceId == request.RequesterDeviceId && d.Role == Role.Admin && d.AdminVerified);
                if (!requesterIsVerifiedAdmin)
                {
                    return;
                }
            }

            var response = BuildResponse(request, identity);

            var responseBytes = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(response));
            await stream.WriteAsync(responseBytes, ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception)
        {
            // best-effort - a failed answer just means the requester treats us as unreachable/granted
        }
    }

    private EditLockResponseMessage BuildResponse(EditLockRequestMessage request, LiveIdentity identity)
    {
        var key = (request.ScopeKind, request.ScopeId);
        var state = _states.GetOrAdd(key, _ => new ScopeLockState());

        if (state.IsAcquiring)
        {
            // We are simultaneously trying to acquire the very same scope - the
            // millisecond-collision case (Abschnitt 5): deny this request AND mark our
            // own in-flight attempt as collided so it also fails and retries.
            state.CollisionDetectedDuringAcquire = true;
            return new EditLockResponseMessage(request.CustomerGroupId, request.ScopeKind, request.ScopeId, false, identity.ComputerName, identity.User);
        }

        if (state.IsHeldByMe)
        {
            return new EditLockResponseMessage(request.CustomerGroupId, request.ScopeKind, request.ScopeId, false, identity.ComputerName, identity.User);
        }

        return new EditLockResponseMessage(request.CustomerGroupId, request.ScopeKind, request.ScopeId, true, string.Empty, string.Empty);
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

        foreach (var state in _states.Values)
        {
            state.InactivityTimer?.Dispose();
        }
    }
}
