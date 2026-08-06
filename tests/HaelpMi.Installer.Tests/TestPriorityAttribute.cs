namespace HaelpMi.Installer.Tests;

/// <summary>
/// Diese Tests bauen bewusst aufeinander auf (installieren, prüfen, deinstallieren/
/// aktualisieren, prüfen weiter) statt unabhängig zu sein - dasselbe Prinzip wie
/// ALPHA-TESTPLAN.md ("Reihenfolge ist absichtlich so gewählt, dass jeder Test auf dem
/// vorherigen aufbaut"). xUnit garantiert innerhalb einer Klasse keine Reihenfolge ohne
/// expliziten <see cref="PriorityOrderer"/> - dieses Attribut liefert dafür die Sortierung.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}
