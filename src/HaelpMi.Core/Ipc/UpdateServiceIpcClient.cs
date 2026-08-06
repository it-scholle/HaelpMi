using System.IO.Pipes;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Ipc;

/// <summary>Vom Agent benutzt, um dem privilegierten HaelpMi.UpdateService etwas anzuweisen (siehe <see cref="UpdateServiceRequest"/>). Gleiches Muster wie <see cref="IpcClient"/>.</summary>
public sealed class UpdateServiceIpcClient
{
    public async Task<UpdateServiceResponse> SendAsync(UpdateServiceRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30); // Install/StartTest können etwas dauern (Datei-I/O, Prozessstart)
        try
        {
            using var client = new NamedPipeClientStream(".", AppConstants.UpdateServiceIpcPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(effectiveTimeout);

            await client.ConnectAsync((int)effectiveTimeout.TotalMilliseconds, timeoutCts.Token);

            var requestBytes = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(request));
            await client.WriteAsync(requestBytes, timeoutCts.Token);
            await client.FlushAsync(timeoutCts.Token);

            using var reader = new StreamReader(client, NetworkSerializer.Encoding, leaveOpen: true);
            var line = await reader.ReadLineAsync(timeoutCts.Token);
            if (line is null)
            {
                return new UpdateServiceResponse(false, "Keine Antwort vom Update-Dienst.");
            }

            return NetworkSerializer.FromJsonLine<UpdateServiceResponse>(line) ?? new UpdateServiceResponse(false, "Ungültige Antwort vom Update-Dienst.");
        }
        catch (Exception ex)
        {
            return new UpdateServiceResponse(false, $"Update-Dienst nicht erreichbar: {ex.Message}");
        }
    }
}
