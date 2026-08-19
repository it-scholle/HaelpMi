using System.Diagnostics;
using HaelpMi.Core.Diagnostics;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Fix 19.08.2026 (P1: Config-Sync/Alarm-Ack-Hänger über TestLogger.LogAction ->
/// SharedLogPaths.ResolveDirectory, siehe dortiger Klassenkommentar) - deckt genau die
/// Zeitschranke ab, die vorher fehlte: ein unerreichbares Netzlaufwerk darf den
/// Aufruferthread nur noch für die kurze Probe-Zeit blockieren, nie für die volle
/// (Sekunden bis Minuten dauernde) SMB-Timeout-Zeit des Betriebssystems.
/// </summary>
public class SharedLogPathsTests
{
    // Nicht gerouteter Adressbereich (RFC 5737/TEST-NET-artig, hier bewusst eine private
    // Adresse ohne echtes Ziel im Testnetz) - erzeugt einen echten, aber langsamen
    // Verbindungsversuch, genau das Verhalten eines gemappten, aber gerade toten Z:.
    private const string UnreachableUncPath = @"\\10.255.255.1\HaelpMi-Logs-Unreachable-Test";

    [Fact]
    public void ProbeReachability_UnreachablePath_ReturnsWithinBoundedTime_NotOsTimeout()
    {
        var stopwatch = Stopwatch.StartNew();
        var reachable = SharedLogPaths.ProbeReachability(UnreachableUncPath);
        stopwatch.Stop();

        Assert.False(reachable);
        // Grosszuegiger Puffer ueber dem 300ms-Probe-Timeout (CI-Jitter, Thread-Pool-Anlauf),
        // aber weit unter dem, was ein echter SMB-Verbindungsaufbau-Timeout braucht (typisch
        // 20s+) - genau das ist der Regressionsschutz gegen den urspruenglichen Bug.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"ProbeReachability blockierte {stopwatch.Elapsed.TotalMilliseconds}ms - Zeitschranke greift nicht mehr.");
    }

    [Fact]
    public void ResolveDirectory_CachesResult_DoesNotReprobeWithinRecheckInterval()
    {
        SharedLogPaths.ResetReachabilityCacheForTests();
        try
        {
            var first = SharedLogPaths.ResolveDirectory(Path.GetTempPath());

            var stopwatch = Stopwatch.StartNew();
            var second = SharedLogPaths.ResolveDirectory(Path.GetTempPath());
            stopwatch.Stop();

            Assert.Equal(first, second);
            // Der zweite Aufruf muss aus dem Cache kommen (keine erneute Probe) - deutlich
            // schneller als jede Netzwerk-Operation, auch eine erfolgreiche.
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(50),
                $"Zweiter ResolveDirectory-Aufruf dauerte {stopwatch.Elapsed.TotalMilliseconds}ms - Cache greift nicht.");
        }
        finally
        {
            SharedLogPaths.ResetReachabilityCacheForTests();
        }
    }
}
