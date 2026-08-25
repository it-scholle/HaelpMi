using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Ersetzt den direkten <see cref="AlarmTcpListener"/>-Aufruf in HaelpMi.Agent (Issue #9,
/// Fast User Switching ohne Logout/Reboot): genau eine Sitzung auf einer Maschine kann
/// den echten Alarm-TCP-Port halten ("Primary"). Jede weitere angemeldete Sitzung
/// ("Satellite") verbindet sich stattdessen über einen lokalen Relay-Kanal
/// (<see cref="AlarmRelayServer"/>/<see cref="AlarmRelayClient"/>) mit der Primary, damit
/// auch dort Popup+Ton in der jeweils EIGENEN interaktiven Sitzung gezeigt werden können
/// (Session-0-Grund wie beim Rest des Agents - eine fremde Sitzung kann keine UI in einer
/// anderen zeigen). Meldet sich die Primary-Sitzung komplett ab (nicht nur Sitzungswechsel),
/// bricht der Relay-Kanal ab und die verbliebenen Satellites konkurrieren erneut um den
/// jetzt freien TCP-Port - wer zuerst bindet, wird die neue Primary.
/// </summary>
public sealed class AlarmChannel : IAsyncDisposable
{
    private readonly AlarmTcpListener _tcpListener;
    private readonly Action<string>? _audit;

    private AlarmRelayServer? _relayServer;
    private AlarmRelayClient? _relayClient;
    private CancellationTokenSource? _cts;
    private Task? _satelliteLoop;

    public event EventHandler<AlarmReceivedEventArgs>? AlarmReceived;

    public bool IsPrimary { get; private set; }

    public AlarmChannel(Func<LiveIdentity> identityProvider, Action<string>? audit = null)
    {
        _tcpListener = new AlarmTcpListener(identityProvider, audit);
        // Einmal registriert, unabhängig von der Rolle - feuert für eine Satellite-Instanz
        // ohnehin nie, da deren _tcpListener.Start() nie erfolgreich bindet.
        _tcpListener.AlarmReceived += OnPrimaryAlarmReceived;
        _audit = audit;
    }

    public void Start(int port = Models.AppConstants.AlarmTcpPort)
    {
        if (_tcpListener.Start(port))
        {
            PromoteToPrimary();
            return;
        }

        _cts = new CancellationTokenSource();
        _satelliteLoop = RunAsSatelliteAsync(port, _cts.Token);
    }

    private void PromoteToPrimary()
    {
        IsPrimary = true;
        _relayServer = new AlarmRelayServer();
        _relayServer.Start();
        _audit?.Invoke("Alarm-Primary-Instanz aktiv (hält den TCP-Port, reicht an Satellites weiter).");
    }

    private void OnPrimaryAlarmReceived(object? sender, AlarmReceivedEventArgs e)
    {
        AlarmReceived?.Invoke(this, e);

        if (_relayServer is not null)
        {
            var relayMessage = new AlarmRelayMessage(e.Request, e.SenderAddress.ToString());
            _ = _relayServer.BroadcastAsync(relayMessage, CancellationToken.None);
        }
    }

    private async Task RunAsSatelliteAsync(int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var client = new AlarmRelayClient();
            client.AlarmReceived += OnSatelliteAlarmReceived;

            var disconnected = new TaskCompletionSource();
            client.Disconnected += (_, _) => disconnected.TrySetResult();

            var connected = await client.TryConnectAsync(TimeSpan.FromSeconds(5), ct);
            if (connected)
            {
                _relayClient = client;
                _audit?.Invoke("Alarm-Satellite-Instanz verbunden, empfängt Alarme über lokalen Relay-Kanal.");

                using var reg = ct.Register(() => disconnected.TrySetCanceled());
                try
                {
                    await disconnected.Task;
                }
                catch (OperationCanceledException)
                {
                    return; // regulärer Shutdown
                }
                finally
                {
                    _relayClient = null;
                }

                _audit?.Invoke("Verbindung zur Alarm-Primary-Instanz verloren - versuche Übernahme.");
            }

            client.AlarmReceived -= OnSatelliteAlarmReceived;

            if (_tcpListener.Start(port))
            {
                PromoteToPrimary();
                return;
            }

            // Weder Primary erreichbar noch selbst Bind erfolgreich (kollidiert gerade mit
            // einer zeitgleich anlaufenden weiteren Sitzung) - zufälliger Backoff wie beim
            // Edit-Lock-Kollisionsfall (AppConstants.EditLockCollisionBackoff), dann erneut
            // versuchen.
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(500, 2000)), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnSatelliteAlarmReceived(object? sender, AlarmReceivedEventArgs e) => AlarmReceived?.Invoke(this, e);

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_satelliteLoop is not null)
        {
            try { await _satelliteLoop; } catch { /* already handled inside the loop */ }
        }
        _cts?.Dispose();

        await _tcpListener.DisposeAsync();
        if (_relayServer is not null)
        {
            await _relayServer.DisposeAsync();
        }
        if (_relayClient is not null)
        {
            await _relayClient.DisposeAsync();
        }
    }
}
