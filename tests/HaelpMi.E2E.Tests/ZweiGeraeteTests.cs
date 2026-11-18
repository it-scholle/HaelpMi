using HaelpMi.Installer.Tests;
using Xunit;

namespace HaelpMi.E2E.Tests;

/// <summary>Tests mit Teilen auf A und B, abgestimmt über <see cref="Rendezvous"/>.</summary>
[Collection(E2ESammlung.Name)]
[TestCaseOrderer("HaelpMi.Installer.Tests.PriorityOrderer", "HaelpMi.E2E.Tests")]
[Trait("Category", "E2E")]
[Trait("Geraet", "A")]
[Trait("Geraet", "B")]
public class ZweiGeraeteTests
{
    // B meldet sich per Boot-Call und erscheint im Dashboard von A.
    [Fact(Skip = "folgt mit Commit-Einheit 4 (#164)"), TestPriority(40)]
    public void T4_BootCall_BErscheintImDashboardVonA()
    {
    }

    // A löst per Hotkey aus, B zeigt Raum und Nummer, "Bin unterwegs" auf B kommt auf A an, Fenster wird schließbar (#136).
    [Fact(Skip = "folgt mit Commit-Einheit 4 (#164)"), TestPriority(50)]
    public void T5_Alarm_AnachB_MitRueckmeldung()
    {
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public class E2ESammlung
{
    public const string Name = "E2E";
}
