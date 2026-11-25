using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Läuft nur auf der Primary-Agent-Instanz (siehe <see cref="AlarmChannel"/>): reicht
/// jeden über den echten TCP-Alarmkanal empfangenen Alarm zusätzlich an alle verbundenen
/// Satellite-Instanzen derselben Maschine weiter (Issue #9, Fast User Switching ohne
/// Logout/Reboot) - die können den Text/Ton nicht selbst über TCP empfangen, weil der
/// Port bereits von der Primary belegt ist.
///
/// Named Pipes sind (anders als "Local\"-Mutexe) sitzungsübergreifend sichtbar, aber die
/// Standard-DACL erlaubt nur dem erzeugenden Konto den Zugriff - eine Satellite-Instanz
/// läuft aber als ANDERER Windows-Nutzer in einer anderen Sitzung. Deshalb hier explizit
/// BUILTIN\Users freigegeben, gleiche wohlbekannte SID wie in AutostartRegistrar.
///
/// Rückweg: Ein Satellite meldet per <see cref="AlarmRelayReply"/>, ob seine Sitzung den Alarm
/// angezeigt hat, damit die Primary erst dann quittiert. Satellites, die sich nicht als
/// antwortfähig angemeldet haben (Verbindung nur lesend), zählen wie vor dieser Rückmeldung
/// als „angezeigt“ - lieber die alte Ungenauigkeit als eine fehlende Quittung.
/// </summary>
public sealed class AlarmRelayServer : IAsyncDisposable
{
    private const string BuiltInUsersGroupSid = "S-1-5-32-545";

    private readonly ConcurrentDictionary<Guid, RelayClient> _clients = new();
    private readonly ConcurrentDictionary<Guid, DisplayWaiter> _waiters = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public bool HasClients => !_clients.IsEmpty;

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var server = CreatePipeServer();
            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                return;
            }
            catch (IOException)
            {
                server.Dispose();
                continue;
            }

            var id = Guid.NewGuid();
            var client = new RelayClient(server);
            _clients[id] = client;
            _ = ReadRepliesAsync(id, client, ct);
        }
    }

    private static NamedPipeServerStream CreatePipeServer()
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(BuiltInUsersGroupSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            AppConstants.AlarmRelayPipeName, PipeDirection.InOut, maxNumberOfServerInstances: 20,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, pipeSecurity);
    }

    /// <summary>
    /// Liest die Rückmeldungen eines Satellites. Die Daten kommen aus einer fremden Windows-Sitzung:
    /// begrenzte Zeilen, unlesbare Zeilen werden übersprungen. Endet die Verbindung, fällt der Client weg.
    /// </summary>
    private async Task ReadRepliesAsync(Guid id, RelayClient client, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await BoundedLineReader.ReadLineAsync(client.Pipe, ct);
                if (line is null)
                {
                    break;
                }

                AlarmRelayReply? reply;
                try
                {
                    reply = NetworkSerializer.FromJsonLine<AlarmRelayReply>(line);
                }
                catch (Exception)
                {
                    continue;
                }

                if (reply is null)
                {
                    continue;
                }

                if (reply.RelayId == Guid.Empty)
                {
                    client.CanReply = true;
                }
                else if (_waiters.TryGetValue(reply.RelayId, out var waiter))
                {
                    waiter.Reply(reply.Displayed);
                }
            }
        }
        catch (Exception)
        {
            // Verbindung abgerissen (Sitzung abgemeldet) - wie ein reguläres Ende behandeln
        }

        if (!ct.IsCancellationRequested)
        {
            RemoveClient(id);
        }
    }

    /// <summary>
    /// Reicht den Alarm an alle Satellites weiter und wartet höchstens <paramref name="timeout"/>, ob einer ihn anzeigt.
    /// Best-effort: ein einzelner tot gegangener Satellite (Sitzung inzwischen abgemeldet) darf die anderen nicht
    /// blockieren, deshalb pro Client eigenes try/catch.
    /// </summary>
    /// <returns>true, sobald ein Satellite die Anzeige meldet oder ein nicht antwortfähiger erreicht wurde; sonst false.</returns>
    public async Task<bool> BroadcastAndAwaitDisplayAsync(AlarmRelayMessage message, TimeSpan timeout, CancellationToken ct)
    {
        var waiter = new DisplayWaiter();
        _waiters[message.RelayId] = waiter;
        try
        {
            var line = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(message));
            var reached = 0;
            var reachedWithoutReply = false;
            foreach (var (id, client) in _clients)
            {
                try
                {
                    await client.Pipe.WriteAsync(line, ct);
                    await client.Pipe.FlushAsync(ct);
                    reached++;
                    reachedWithoutReply |= !client.CanReply;
                }
                catch (Exception)
                {
                    RemoveClient(id);
                }
            }

            if (reachedWithoutReply)
            {
                return true;
            }

            if (reached == 0)
            {
                return false;
            }

            waiter.ExpectReplies(reached);
            try
            {
                return await waiter.Task.WaitAsync(timeout, ct);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
        finally
        {
            _waiters.TryRemove(message.RelayId, out _);
        }
    }

    private void RemoveClient(Guid id)
    {
        if (_clients.TryRemove(id, out var client))
        {
            client.Pipe.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* already handled inside the loop */ }
        }
        _cts?.Dispose();

        foreach (var client in _clients.Values)
        {
            client.Pipe.Dispose();
        }
        _clients.Clear();
    }

    private sealed class RelayClient(NamedPipeServerStream pipe)
    {
        public NamedPipeServerStream Pipe { get; } = pipe;

        public volatile bool CanReply;
    }

    /// <summary>Sammelt die Antworten einer Weiterleitung: fertig beim ersten „angezeigt“ oder wenn alle „nein“ gesagt haben.</summary>
    private sealed class DisplayWaiter
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _expected = -1;
        private int _replies;

        public Task<bool> Task => _result.Task;

        public void Reply(bool displayed)
        {
            lock (_gate)
            {
                _replies++;
                if (displayed)
                {
                    _result.TrySetResult(true);
                }
                else if (_expected >= 0 && _replies >= _expected)
                {
                    _result.TrySetResult(false);
                }
            }
        }

        public void ExpectReplies(int expected)
        {
            lock (_gate)
            {
                _expected = expected;
                if (_replies >= expected)
                {
                    _result.TrySetResult(false);
                }
            }
        }
    }
}
