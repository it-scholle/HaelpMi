using System.Diagnostics;
using System.IO.Pipes;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Ipc;

/// <summary>
/// Used by HaelpMi.Config to ask the running HaelpMi.Agent to do something (see
/// <see cref="IpcServer"/>). Verbindet sich sitzungsgebunden (siehe
/// <see cref="IpcPipeNaming"/>) - Config läuft immer in derselben Sitzung wie der Agent,
/// den es erreichen soll, die eigene <see cref="Process.SessionId"/> reicht daher aus.
/// </summary>
public sealed class IpcClient
{
    public async Task<IpcResponse> SendAsync(IpcCommandType command, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        try
        {
            var pipeName = IpcPipeNaming.BuildSessionScopedPipeName(Process.GetCurrentProcess().SessionId);
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(effectiveTimeout);

            // ConfigureAwait(false) ist hier PFLICHT, nicht Stil: HaelpMi.Config ruft dies
            // aus OnStartup heraus per .GetAwaiter().GetResult() synchron blockierend auf
            // (EnsureAgentIsRunningAsync). Ohne ConfigureAwait(false) versucht jede
            // Fortsetzung hier, auf den WPF-UI-Thread zurückzukehren - der aber gerade genau
            // auf diese Fortsetzung wartet. Kompletter, stiller Deadlock (kein Absturz, kein
            // Log-Eintrag, kein Fenster) - entdeckt 04.08.2026 beim "Dashboard startet nicht"-
            // Vorfall, siehe HaelpMi.Config/App.xaml.cs.
            await client.ConnectAsync((int)effectiveTimeout.TotalMilliseconds, timeoutCts.Token).ConfigureAwait(false);

            var request = new IpcRequest(command);
            var requestBytes = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(request));
            await client.WriteAsync(requestBytes, timeoutCts.Token).ConfigureAwait(false);
            await client.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            using var reader = new StreamReader(client, NetworkSerializer.Encoding, leaveOpen: true);
            var line = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            if (line is null)
            {
                return new IpcResponse(false, "Keine Antwort vom Hintergrunddienst.");
            }

            return NetworkSerializer.FromJsonLine<IpcResponse>(line) ?? new IpcResponse(false, "Ungültige Antwort vom Hintergrunddienst.");
        }
        catch (Exception ex)
        {
            return new IpcResponse(false, $"Hintergrunddienst nicht erreichbar: {ex.Message}");
        }
    }
}
