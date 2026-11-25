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
///
/// Nach jedem Alarm meldet der Client per <see cref="AlarmRelayReply"/> zurück, ob seine Sitzung
/// ihn angezeigt hat. Lässt die Pipe nur lesenden Zugriff zu, verbindet er sich nur lesend;
/// die Primary behandelt ihn dann als „angezeigt“.
/// </summary>
public sealed class AlarmRelayClient : IAsyncDisposable
{
    public event EventHandler<AlarmReceivedEventArgs>? AlarmReceived;
    public event EventHandler? Disconnected;

    /// <summary>false, wenn die Verbindung nur lesend steht; die Primary zählt Alarme dann ungeprüft als angezeigt.</summary>
    public bool CanReply { get; private set; }

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NamedPipeClientStream? _pipe;

    private CancellationTokenSource? _cts;
    private Task? _readLoop;

    /// <summary>Liefert true, sobald eine Primary-Instanz erreichbar war und die Verbindung stand.</summary>
    public async Task<bool> TryConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        var (pipe, accessDenied) = await TryOpenAsync(PipeDirection.InOut, timeout, ct);
        CanReply = pipe is not null;
        if (accessDenied)
        {
            (pipe, _) = await TryOpenAsync(PipeDirection.In, timeout, ct);
        }

        if (pipe is null)
        {
            return false; // keine Primary-Instanz erreichbar (noch keine da, oder gerade zwischen Übernahmen)
        }

        _pipe = pipe;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (CanReply)
        {
            await WriteReplyAsync(new AlarmRelayReply(Guid.Empty, false), _cts.Token);
        }

        _readLoop = ReadLoopAsync(pipe, _cts.Token);
        return true;
    }

    /// <returns>
    /// Pipe null, wenn keine Primary erreichbar ist. AccessDenied: Die Pipe erlaubt diese Richtung nicht
    /// (ältere Primary mit reiner Ausgabe-Pipe, oder fehlende Schreibrechte aus einer fremden Sitzung).
    /// </returns>
    private static async Task<(NamedPipeClientStream? Pipe, bool AccessDenied)> TryOpenAsync(PipeDirection direction, TimeSpan timeout, CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", AppConstants.AlarmRelayPipeName, direction, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds, ct);
            return (pipe, false);
        }
        catch (UnauthorizedAccessException)
        {
            pipe.Dispose();
            return (null, true);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            pipe.Dispose();
            return (null, false);
        }
    }

    private async Task ReplyAsync(AlarmReceivedEventArgs args, Guid relayId, CancellationToken ct)
    {
        var displayed = await args.WasDisplayedAsync();
        await WriteReplyAsync(new AlarmRelayReply(relayId, displayed), ct);
    }

    private async Task WriteReplyAsync(AlarmRelayReply reply, CancellationToken ct)
    {
        var pipe = _pipe;
        if (pipe is null)
        {
            return;
        }

        var line = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(reply));
        try
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                await pipe.WriteAsync(line, ct);
                await pipe.FlushAsync(ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Pipe abgerissen oder Client beendet: die Leseschleife meldet das als Disconnected,
            // die Primary wertet die fehlende Antwort nach ihrer Frist als „nicht angezeigt“.
        }
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
                var args = new AlarmReceivedEventArgs { Request = relayed.Request, SenderAddress = senderAddress };
                AlarmReceived?.Invoke(this, args);
                if (CanReply && relayed.RelayId != Guid.Empty)
                {
                    _ = ReplyAsync(args, relayed.RelayId, ct);
                }
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
        _writeLock.Dispose();
    }
}
