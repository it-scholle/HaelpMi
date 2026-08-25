using System.IO.Pipes;
using System.Net;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Läuft auf einer Satellite-Agent-Instanz (siehe <see cref="AlarmChannel"/>): verbindet
/// sich mit der Primary-Instanz derselben Maschine und empfängt von dort weitergereichte
/// Alarme (Issue #9). Verbindungsverlust (Primary-Sitzung abgemeldet) wird über
/// <see cref="Disconnected"/> gemeldet, statt selbst einen Reconnect zu versuchen - die
/// Entscheidung "erneut als Satellite verbinden oder selbst Primary werden" trifft
/// <see cref="AlarmChannel"/>.
/// </summary>
public sealed class AlarmRelayClient : IAsyncDisposable
{
    public event EventHandler<AlarmReceivedEventArgs>? AlarmReceived;
    public event EventHandler? Disconnected;

    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _cts;
    private Task? _readLoop;

    /// <summary>Liefert true, sobald eine Primary-Instanz erreichbar war und die Verbindung stand.</summary>
    public async Task<bool> TryConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", AppConstants.AlarmRelayPipeName, PipeDirection.In, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds, ct);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            pipe.Dispose();
            return false; // keine Primary-Instanz erreichbar (noch keine da, oder gerade zwischen Übernahmen)
        }

        _pipe = pipe;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readLoop = ReadLoopAsync(pipe, _cts.Token);
        return true;
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(pipe, NetworkSerializer.Encoding, leaveOpen: true);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null)
                {
                    break; // Primary hat die Verbindung geschlossen (Sitzung abgemeldet)
                }

                AlarmRelayMessage? relayed;
                try
                {
                    relayed = NetworkSerializer.FromJsonLine<AlarmRelayMessage>(line);
                }
                catch (Exception)
                {
                    continue; // ein einzelner unlesbarer Frame darf die Verbindung nicht abreißen lassen
                }

                if (relayed is null)
                {
                    continue;
                }

                var senderAddress = IPAddress.TryParse(relayed.SenderAddress, out var parsed) ? parsed : IPAddress.None;
                AlarmReceived?.Invoke(this, new AlarmReceivedEventArgs { Request = relayed.Request, SenderAddress = senderAddress });
            }
        }
        catch (OperationCanceledException)
        {
            return; // regulärer Shutdown, kein Disconnected-Ereignis
        }
        catch (Exception)
        {
            // Pipe abgerissen - wie ein regulär geschlossenes Ende behandeln, siehe unten
        }

        if (!ct.IsCancellationRequested)
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_readLoop is not null)
        {
            try { await _readLoop; } catch { /* already handled inside the loop */ }
        }
        _cts?.Dispose();
        _pipe?.Dispose();
    }
}
