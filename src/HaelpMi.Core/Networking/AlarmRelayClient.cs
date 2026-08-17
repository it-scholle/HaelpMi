using System.IO.Pipes;
using System.Net;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Gegenstück zu <see cref="AlarmRelayServer"/> (s. dortige Klassendoku für den vollen
/// Fast-User-Switching-Hintergrund): läuft in jeder "Satellite"-Agent-Instanz, also einer
/// Sitzung, die den exklusiven Alarm-Port nicht selbst binden konnte. Hält eine dauerhafte
/// Verbindung zur Primary-Instanz; jeder weitergereichte Alarm wird über
/// <see cref="AlarmRelayed"/> exakt wie ein direkt empfangener behandelt (s.
/// AlarmFlowCoordinator.HandleIncomingAlarmRequest).
///
/// Bricht die Verbindung ab oder kommt sie gar nicht erst zustande (z. B. weil die
/// Primary-Instanz gerade selbst noch startet), feuert <see cref="ConnectionLost"/> - der
/// Aufrufer (HaelpMi.Agent) versucht daraufhin nach kurzem Zufalls-Backoff erneut, selbst
/// Primary zu werden (s. AlarmTcpListener.Start-Rückgabewert), bevor er es erneut hier probiert.
/// Bewusst einmaliger Verbindungsversuch pro Start() - kein eigenes Retry hier, das
/// übernimmt der Aufrufer zusammen mit der Bind-Übernahme, damit beide Pfade (Primary werden
/// vs. Satellite bleiben) an einer Stelle entschieden werden.
/// </summary>
public sealed class AlarmRelayClient : IAsyncDisposable
{
    public event EventHandler<AlarmReceivedEventArgs>? AlarmRelayed;
    public event EventHandler? ConnectionLost;

    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _receiveLoop = ReceiveLoopAsync(_cts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", AppConstants.AlarmRelayPipeName, PipeDirection.In, PipeOptions.Asynchronous);
            await client.ConnectAsync(2000, ct);

            using var reader = new StreamReader(client, NetworkSerializer.Encoding, leaveOpen: true);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null)
                {
                    break; // Primary hat die Pipe geschlossen (Sitzung abgemeldet/Prozess beendet)
                }

                AlarmRelayMessage? relay;
                try
                {
                    relay = NetworkSerializer.FromJsonLine<AlarmRelayMessage>(line);
                }
                catch (Exception)
                {
                    continue; // unerwartetes Format - eine Zeile überspringen, Verbindung bleibt bestehen
                }

                if (relay is null)
                {
                    continue;
                }

                var senderAddress = IPAddress.TryParse(relay.SenderAddress, out var parsed) ? parsed : IPAddress.None;
                AlarmRelayed?.Invoke(this, new AlarmReceivedEventArgs { Request = relay.Request, SenderAddress = senderAddress });
            }
        }
        catch (OperationCanceledException)
        {
            return; // reguläres Beenden (DisposeAsync) - kein Verbindungsabbruch, kein ConnectionLost nötig
        }
        catch (Exception)
        {
            // Verbindungsaufbau fehlgeschlagen (keine Primary-Instanz erreichbar, z. B. während
            // des Rollenwechsels) oder eine bestehende Verbindung ist abgebrochen - beides
            // behandelt der Aufrufer unten identisch.
        }

        if (!ct.IsCancellationRequested)
        {
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop; } catch (Exception) { /* already handled inside the loop */ }
        }
        _cts?.Dispose();
    }
}
