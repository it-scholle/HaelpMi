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
///
/// Nachlieferung an frisch verbundene Satellites (Issue #38): anders als eine Alarm-Anfrage
/// (die sich bei einem knapp verpassten Broadcast beim nächsten 5-Sekunden-Repeat von selbst
/// heilt) ist "bin unterwegs" ein EINMALIGES Ereignis - trifft es ein, während eine Satellite-
/// Sitzung ihren Relay-Client gerade erst verbindet (direkt nach einem Fast User Switch der
/// Normalfall), wäre es ohne diesen Puffer endgültig verloren. <see cref="AcceptLoopAsync"/>
/// liefert einer neu verbundenen Sitzung deshalb erst den kurzen Rückstand nach, bevor sie
/// überhaupt in <see cref="_clients"/> aufgenommen wird - Duplikate durch die daraus
/// resultierende Überschneidung sind unkritisch, beide Empfänger (RepeatingAlarmSession/
/// AlarmFlowCoordinator) verarbeiten dieselbe Nachricht bereits idempotent.
/// </summary>
public sealed class AlarmFeedbackRelayServer : IAsyncDisposable
{
    private const string BuiltInUsersGroupSid = "S-1-5-32-545";

    private static readonly TimeSpan ReplayWindow = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, NamedPipeServerStream> _clients = new();
    private readonly ConcurrentQueue<(DateTimeOffset SentAtUtc, AlarmFeedbackEnvelope Envelope)> _recentBroadcasts = new();
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

            // Rückstand nachliefern, BEVOR diese Sitzung in _clients aufgenommen wird - so
            // kann kein gleichzeitiger BroadcastAsync-Aufruf parallel in denselben Pipe-Stream
            // schreiben (NamedPipeServerStream ist nicht für nebenläufige Writer ausgelegt).
            // Ein Broadcast, der exakt in der Mikrosekunden-Lücke zwischen Nachlieferung und
            // Registrierung eintrifft, bliebe theoretisch weiterhin verpasst - ungleich
            // unwahrscheinlicher als das ursprüngliche, mehrere Sekunden lange Verbindungs-
            // fenster, das dieser Fix schließt.
            if (!await TryReplayRecentBroadcastsAsync(server, ct))
            {
                continue; // Sitzung ist schon während der Nachlieferung wieder weg
            }

            _clients[Guid.NewGuid()] = server;
        }
    }

    private async Task<bool> TryReplayRecentBroadcastsAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - ReplayWindow;
        foreach (var (sentAtUtc, envelope) in _recentBroadcasts)
        {
            if (sentAtUtc < cutoff)
            {
                continue;
            }

            try
            {
                var line = NetworkSerializer.Encoding.GetBytes(NetworkSerializer.ToJsonLine(envelope));
                await server.WriteAsync(line, ct);
                await server.FlushAsync(ct);
            }
            catch (Exception)
            {
                server.Dispose();
                return false;
            }
        }

        return true;
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
        _recentBroadcasts.Enqueue((DateTimeOffset.UtcNow, envelope));
        TrimRecentBroadcasts();

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

    // Läuft nur hier, bei jedem neuen Eintrag - die Warteschlange bleibt dadurch beschränkt,
    // ohne einen eigenen Timer zu brauchen (ConcurrentQueue ist FIFO, das älteste Element
    // steht also garantiert vorn).
    private void TrimRecentBroadcasts()
    {
        var cutoff = DateTimeOffset.UtcNow - ReplayWindow;
        while (_recentBroadcasts.TryPeek(out var oldest) && oldest.SentAtUtc < cutoff)
        {
            _recentBroadcasts.TryDequeue(out _);
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
