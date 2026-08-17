using System.Collections.Concurrent;
using System.IO.Pipes;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Fast-User-Switching-Fix 17.08.2026 (Kernanforderung: jede angemeldete Windows-Sitzung muss
/// zuverlässig Alarme empfangen, auch bei Benutzerwechsel ohne Ab-/Anmeldung): gehostet
/// ausschließlich von der Agent-Instanz, die den exklusiven <see cref="AppConstants.AlarmTcpPort"/>
/// tatsächlich binden konnte ("Primary", s. AlarmTcpListener.Start-Rückgabewert) - reicht jeden
/// empfangenen Alarm zusätzlich lokal an alle "Satellite"-Instanzen weiter, die in anderen
/// gleichzeitig angemeldeten Sitzungen derselben Maschine laufen (s. AlarmRelayClient).
///
/// Bewusst rein lokal (Named Pipe, kein Netzwerkverkehr) - kein Widerspruch zum P2P-/
/// Kein-zentraler-Server-Prinzip aus CLAUDE.md, das sich auf die Kommunikation ZWISCHEN Geräten
/// bezieht, nicht auf mehrere Sitzungen derselben Maschine. Kein eigenes Wahlprotokoll: wer
/// Primary wird, entscheidet allein der exklusive TCP-Bind (Betriebssystem-Garantie).
/// </summary>
public sealed class AlarmRelayServer : IAsyncDisposable
{
    // ConcurrentDictionary nur als nebenläufig-sichere Menge genutzt (Value ohne Bedeutung) -
    // gleiches Muster wie an anderen Stellen im Projekt, an denen es kein passendes
    // ConcurrentSet gibt.
    private readonly ConcurrentDictionary<NamedPipeServerStream, byte> _clients = new();
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

    // Gleiches Accept-Loop-Muster wie IpcServer: nach jeder angenommenen Verbindung sofort eine
    // neue wartende Pipe-Instanz aufmachen, damit mehrere Satellites (mehrere gleichzeitig
    // angemeldete Sitzungen) unabhängig voneinander verbunden bleiben können.
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                AppConstants.AlarmRelayPipeName, PipeDirection.Out, maxNumberOfServerInstances: 10,
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

            _clients[server] = 0;
        }
    }

    /// <summary>Vom AlarmReceived-Handler der Primary-Instanz aufgerufen (s. HaelpMi.Agent/App.xaml.cs).</summary>
    public async Task BroadcastAsync(AlarmRequestMessage request, string senderAddress)
    {
        if (_clients.IsEmpty)
        {
            return; // keine Satellites verbunden - häufigster Fall (Ein-Sitzungs-Maschine)
        }

        var payload = NetworkSerializer.Encoding.GetBytes(
            NetworkSerializer.ToJsonLine(new AlarmRelayMessage(request, senderAddress)));

        foreach (var client in _clients.Keys)
        {
            try
            {
                await client.WriteAsync(payload);
                await client.FlushAsync();
            }
            catch (Exception)
            {
                // Satellite ist weg (Sitzung abgemeldet, Prozess beendet o. ä.) - Stream
                // aufräumen, darf die Zustellung an die übrigen Satellites nicht verhindern.
                _clients.TryRemove(client, out _);
                try { client.Dispose(); } catch (Exception) { /* best-effort */ }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch (Exception) { /* already handled inside the loop */ }
        }
        _cts?.Dispose();

        foreach (var client in _clients.Keys)
        {
            try { client.Dispose(); } catch (Exception) { /* best-effort */ }
        }
        _clients.Clear();
    }
}
