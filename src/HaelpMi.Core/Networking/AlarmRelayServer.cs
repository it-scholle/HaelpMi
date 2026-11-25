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
/// antwortfähig angemeldet haben (Verbindung nur lesend), zählen als „angezeigt“ - lieber eine
/// ungeprüfte Quittung als eine fehlende.
/// </summary>
public sealed class AlarmRelayServer : IAsyncDisposable
{
    private const string BuiltInUsersGroupSid = "S-1-5-32-545";

    private readonly Action<string>? _audit;
    private readonly ConcurrentDictionary<Guid, RelayClient> _clients = new();
    private readonly ConcurrentDictionary<Guid, DisplayWaiter> _waiters = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public AlarmRelayServer(Action<string>? audit = null)
    {
        _audit = audit;
    }

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
                    _audit?.Invoke("Alarm-Relay: unlesbare Antwort eines Satellites verworfen.");
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
                    waiter.Reply(id, reply.Displayed);
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
    /// Reicht den Alarm an alle Satellites weiter und wartet insgesamt höchstens <paramref name="timeout"/>, ob einer
    /// ihn anzeigt. Die Frist umfasst auch das Schreiben: Ein Satellite, der gerade nicht liest, darf weder die anderen
    /// noch die Quittung an den Sender aufhalten, deshalb parallel und pro Client eigenes try/catch.
    /// </summary>
    /// <returns>true, sobald ein Satellite die Anzeige meldet oder ein nicht antwortfähiger erreicht wurde; sonst false.</returns>
    public async Task<bool> BroadcastAndAwaitDisplayAsync(AlarmRelayMessage message, TimeSpan timeout, CancellationToken ct)
    {
        var waiter = new DisplayWaiter();
        _waiters[message.RelayId] = waiter;
        // Eigene Frist nur für die Schreibvorgänge; sie wird erst freigegeben, wenn alle beendet sind.
        var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        writeDeadline.CancelAfter(timeout);
        try
        {
            var line = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(message));
            var clients = _clients.ToArray();
            waiter.ExpectReplies(clients.Length);
            var writes = clients.Select(c => WriteAsync(c.Key, c.Value, line, waiter, writeDeadline.Token)).ToArray();
            _ = Task.WhenAll(writes).ContinueWith(_ => writeDeadline.Dispose(), TaskScheduler.Default);
            if (clients.Length == 0)
            {
                return false;
            }

            return await waiter.Task.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            _waiters.TryRemove(message.RelayId, out _);
        }
    }

    private async Task WriteAsync(Guid id, RelayClient client, byte[] line, DisplayWaiter waiter, CancellationToken ct)
    {
        try
        {
            // Gleichzeitige Weiterleitungen dürfen ihre Zeilen auf derselben Pipe nicht verschränken.
            await client.WriteLock.WaitAsync(ct);
            try
            {
                await client.Pipe.WriteAsync(line, ct);
                await client.Pipe.FlushAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Abbruch mitten in der Zeile: der Datenstrom ist nicht mehr sauber, der Satellite verbindet sich neu.
                RemoveClient(id);
                throw;
            }
            finally
            {
                client.WriteLock.Release();
            }

            if (!client.CanReply)
            {
                waiter.Reply(id, true);
            }
        }
        catch (OperationCanceledException)
        {
            // Frist abgelaufen: zählt als „nicht angezeigt“
        }
        catch (Exception)
        {
            RemoveClient(id);
            waiter.Reply(id, false);
        }
    }

    private void RemoveClient(Guid id)
    {
        if (_clients.TryRemove(id, out var client))
        {
            client.Pipe.Dispose();
            _audit?.Invoke("Alarm-Relay: Verbindung zu einem Satellite beendet.");
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

        public SemaphoreSlim WriteLock { get; } = new(1, 1);

        public volatile bool CanReply;
    }

    /// <summary>Sammelt die Antworten einer Weiterleitung: fertig beim ersten „angezeigt“ oder wenn jeder Satellite „nein“ gesagt hat.</summary>
    private sealed class DisplayWaiter
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<Guid> _answered = new();
        private int _expected = int.MaxValue;

        public Task<bool> Task => _result.Task;

        /// <summary>Nur die erste Antwort je Satellite zählt.</summary>
        public void Reply(Guid clientId, bool displayed)
        {
            lock (_gate)
            {
                if (!_answered.Add(clientId))
                {
                    return;
                }

                if (displayed)
                {
                    _result.TrySetResult(true);
                }
                else if (_answered.Count >= _expected)
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
            }
        }
    }
}
