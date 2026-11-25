using HaelpMi.Core.Models;
using HaelpMi.Installer.Tests;
using Xunit;

namespace HaelpMi.E2E.Tests;

// T24 Regressionstest #222: Block-Regeln einer abgebrochenen Windows-Firewall-Abfrage.
public partial class GeraetATests
{
    /// <summary>
    /// Nur auf A: der Admin-Installer deckt beide Programme ab, die Logik steckt im gemeinsamen Skript. Zuletzt, weil die
    /// Reparatur den Agent beendet und die übrigen Teile auf einen laufenden Agent von A angewiesen sind.
    /// </summary>
    [Fact, TestPriority(160), Trait("Bereich", "R222")]
    public Task T24_Regression222_ReparaturEntferntBlockregel() =>
        Artefakte.SichereAsync(nameof(T24_Regression222_ReparaturEntferntBlockregel),
            () => Installation.RepariereMitBlockregelAsync(Role.Admin, "A"));
}
