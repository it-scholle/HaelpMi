using HaelpMi.Installer.Tests;
using Xunit;

namespace HaelpMi.E2E.Tests;

// T25 Regressionstest #229: Installationsdateien vor dem ersten Agent-Start.
public partial class GeraetATests
{
    /// <summary>Direkt nach T1: wertet nur dessen Setup-Log aus, ändert nichts am Gerät.</summary>
    [Fact, TestPriority(12), Trait("Bereich", "R229")]
    public Task T25_Regression229_DateienVorErstemAgentStart() =>
        Artefakte.SichereAsync(nameof(T25_Regression229_DateienVorErstemAgentStart),
            () =>
            {
                Installation.PruefeDateienVorErstemStart("A");
                return Task.CompletedTask;
            });
}
