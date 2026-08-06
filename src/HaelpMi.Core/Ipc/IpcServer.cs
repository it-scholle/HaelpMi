using System.IO.Pipes;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Ipc;

/// <summary>
/// Hosted by HaelpMi.Agent: a local named-pipe request/response server so
/// HaelpMi.Config - a separate process, deliberately (FR-16) - can ask the Agent to
/// re-broadcast, search again, or run a self-test, since the Agent is the process that
/// actually owns the UDP/TCP sockets (5.2).
/// </summary>
public sealed class IpcServer : IAsyncDisposable
{
    private readonly Dictionary<IpcCommandType, Func<Task<IpcResponse>>> _handlers = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public IpcServer()
    {
        On(IpcCommandType.Ping, () => Task.FromResult(new IpcResponse(true)));
    }

    public void On(IpcCommandType command, Func<Task<IpcResponse>> handler) => _handlers[command] = handler;

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
            var server = new NamedPipeServerStream(
                AppConstants.IpcPipeName, PipeDirection.InOut, maxNumberOfServerInstances: 10,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

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

            _ = HandleConnectionAsync(server, ct);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        using var _ = server;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            using var reader = new StreamReader(server, NetworkSerializer.Encoding, leaveOpen: true);
            var line = await reader.ReadLineAsync(timeoutCts.Token);
            if (line is null)
            {
                return;
            }

            IpcRequest? request;
            try
            {
                request = NetworkSerializer.FromJsonLine<IpcRequest>(line);
            }
            catch (Exception)
            {
                request = null;
            }

            var response = await DispatchAsync(request);

            var responseBytes = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(response));
            await server.WriteAsync(responseBytes, ct);
            await server.FlushAsync(ct);
        }
        catch (Exception)
        {
            // one misbehaving IPC client must not take down the accept loop
        }
    }

    private async Task<IpcResponse> DispatchAsync(IpcRequest? request)
    {
        if (request is null)
        {
            return new IpcResponse(false, "Ungültige Anfrage.");
        }

        if (!_handlers.TryGetValue(request.Command, out var handler))
        {
            return new IpcResponse(false, $"Kein Handler für {request.Command}.");
        }

        try
        {
            return await handler();
        }
        catch (Exception ex)
        {
            return new IpcResponse(false, ex.Message);
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
    }
}
