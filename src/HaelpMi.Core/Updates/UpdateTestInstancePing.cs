using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HaelpMi.Core.Updates;

/// <summary>
/// Kleiner Baustein für den lokalen Selbsttest einer frisch installierten, testweise auf
/// einem separaten Port gestarteten Version (siehe HaelpMi.Agent App.xaml.cs,
/// <c>--update-test-port</c>) - geteilt zwischen <see cref="UpdateOrchestrator"/> (Selbsttest
/// nach einem Peer-getriebenen P2P-Pull) und dem separaten HaelpMi.UpdateBootstrapper-Tool
/// (Selbsttest nach dem menschlich angestoßenen Erst-Bootstrap einer Kundengruppe, siehe
/// dessen Klassenkommentar). Bewusst als öffentliche Klasse statt zweimal dupliziert -
/// anders als bei HaelpMi.InstallCreator (das Core absichtlich nicht referenziert, siehe
/// dessen .csproj) referenziert der Bootstrapper Core ohnehin schon für IPC/Crypto/Cache.
/// </summary>
public static class UpdateTestInstancePing
{
    public static int GetEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Mehrere Versuche mit kurzer Pause, weil der neu gestartete Prozess einen Moment
    /// braucht, bis sein Listener steht.
    /// </summary>
    public static async Task<bool> PingAsync(int testPort, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(35));
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(IPAddress.Loopback, testPort, connectCts.Token);

                await using var stream = client.GetStream();
                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var buffer = new byte[8];
                var read = await stream.ReadAsync(buffer, readCts.Token);
                if (read > 0 && Encoding.UTF8.GetString(buffer, 0, read).TrimEnd() == "OK")
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Testinstanz noch nicht bereit oder abgestürzt - kurz warten und erneut versuchen
            }

            await Task.Delay(500);
        }

        return false;
    }
}
