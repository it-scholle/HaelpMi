using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Läuft nur auf der Primary-Instanz des Feedback-Kanals (siehe <see cref="AlarmFeedbackChannel"/>):
/// reicht jeden über den echten TCP-Feedback-Port empfangenen <see cref="AlarmFeedbackEnvelope"/>
/// zusätzlich an alle verbundenen Satellite-Instanzen derselben Maschine weiter (Issue #9-
/// Nachtrag 2) - identisches Muster wie <see cref="AlarmRelayServer"/> für den Alarm-
/// Empfangskanal, hier für "bin unterwegs" + Status-Relay.
/// </summary>
public sealed class AlarmFeedbackRelayServer : IAsyncDisposable
{
    private const string BuiltInUsersGroupSid = "S-1-5-32-545";

    private readonly ConcurrentDictionary<Guid, NamedPipeServerStream> _clients = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

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

            _clients[Guid.NewGuid()] = server;
        }
    }

    private static NamedPipeServerStream CreatePipeServer()
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(BuiltInUsersGroupSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            AppConstants.AlarmFeedbackRelayPipeName, PipeDirection.Out, maxNumberOfServerInstances: 20,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, pipeSecurity);
    }

    /// <summary>Best-effort wie <see cref="AlarmRelayServer.BroadcastAsync"/>: ein toter Satellite darf die anderen nicht blockieren.</summary>
    public async Task BroadcastAsync(AlarmFeedbackEnvelope envelope, CancellationToken ct)
    {
        var line = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(envelope));
        foreach (var (id, server) in _clients)
        {
            try
            {
                await server.WriteAsync(line, ct);
                await server.FlushAsync(ct);
            }
            catch (Exception)
            {
                _clients.TryRemove(id, out _);
                server.Dispose();
            }
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

        foreach (var server in _clients.Values)
        {
            server.Dispose();
        }
        _clients.Clear();
    }
}
