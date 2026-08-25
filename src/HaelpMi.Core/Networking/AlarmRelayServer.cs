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
/// </summary>
public sealed class AlarmRelayServer : IAsyncDisposable
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
            AppConstants.AlarmRelayPipeName, PipeDirection.Out, maxNumberOfServerInstances: 20,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, pipeSecurity);
    }

    /// <summary>
    /// Best-effort: ein einzelner tot gegangener Satellite (Sitzung inzwischen abgemeldet)
    /// darf die anderen nicht blockieren, deshalb pro Client eigenes try/catch statt eines
    /// einzigen für die ganze Schleife.
    /// </summary>
    public async Task BroadcastAsync(AlarmRelayMessage message, CancellationToken ct)
    {
        var line = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(message));
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
