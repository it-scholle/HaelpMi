using HaelpMi.Core.Models;
using HaelpMi.Installer.Tests;
using Xunit;

namespace HaelpMi.E2E.Tests;

/// <summary>Testteil für VM B (User-Installer).</summary>
[Collection(E2ESammlung.Name)]
[TestCaseOrderer("HaelpMi.Installer.Tests.PriorityOrderer", "HaelpMi.E2E.Tests")]
[Trait("Category", "E2E")]
[Trait("Geraet", "B")]
public class GeraetBTests
{
    [Fact, TestPriority(10)]
    [Trait("FR", "FR-3"), Trait("FR", "FR-4"), Trait("FR", "FR-34"), Trait("FR", "FR-35"), Trait("FR", "FR-37"), Trait("FR", "NFR-2")]
    public Task T1_UserInstaller_InstalliertStill() =>
        Artefakte.SichereAsync(nameof(T1_UserInstaller_InstalliertStill),
            () => Installation.InstalliereUndPruefeAsync(Role.User, "B"));
}
