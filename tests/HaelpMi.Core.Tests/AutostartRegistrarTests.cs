using System.Xml.Linq;
using HaelpMi.Core.Autostart;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Deckt <see cref="AutostartRegistrar"/>.BuildTaskXml direkt ab (InternalsVisibleTo, siehe
/// HaelpMi.Core/AssemblyInfo.cs) - der eigentliche schtasks.exe-Aufruf lässt sich hier nicht
/// prüfen (verändert das ausführende System, siehe TEST-STRATEGY.md - das ist
/// HaelpMi.Installer.Tests' Aufgabe), aber die erzeugte Task-XML ist reiner String-Aufbau
/// und deckt genau den Bugfix vom 08.08.2026 ab: früher lief die Registrierung fest an den
/// zuerst anmeldenden Nutzer gebunden, weil kein Principal auf BUILTIN\Users zeigte.
/// </summary>
public class AutostartRegistrarTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void BuildTaskXml_LogonTrigger_HasNoUserId()
    {
        // Kern des Bugfix: KEIN <UserId> im Trigger, sonst gilt die Anmeldung wieder nur für
        // genau einen Nutzer statt für jede Anmeldung, egal welcher Windows-Nutzer.
        var doc = XDocument.Parse(AutostartRegistrar.BuildTaskXml(@"C:\Program Files\HaelpMi\HaelpMi.Agent.exe"));

        var trigger = doc.Root!.Element(Ns + "Triggers")!.Element(Ns + "LogonTrigger")!;

        Assert.Null(trigger.Element(Ns + "UserId"));
        Assert.Equal("true", trigger.Element(Ns + "Enabled")!.Value);
    }

    [Fact]
    public void BuildTaskXml_Principal_PointsAtBuiltInUsersGroup_NotASingleUser()
    {
        var doc = XDocument.Parse(AutostartRegistrar.BuildTaskXml(@"C:\Program Files\HaelpMi\HaelpMi.Agent.exe"));

        var principal = doc.Root!.Element(Ns + "Principals")!.Element(Ns + "Principal")!;

        // Wohlbekannte, sprachunabhängige SID für BUILTIN\Users - siehe
        // AutostartRegistrar.BuiltInUsersGroupSid.
        Assert.Equal("S-1-5-32-545", principal.Element(Ns + "GroupId")!.Value);
        Assert.Null(principal.Element(Ns + "UserId"));
        Assert.Equal("LeastPrivilege", principal.Element(Ns + "RunLevel")!.Value);
    }

    [Fact]
    public void BuildTaskXml_HasConsoleConnectTrigger_ForFastUserSwitchingWithoutUserId()
    {
        // Issue #9: LogonTrigger allein feuert bei Fast User Switching zu einem zweiten
        // Nutzer unzuverlässig - ConsoleConnect deckt genau diesen Fall ab. Wie beim
        // LogonTrigger auch hier kein <UserId>, sonst gilt es wieder nur für einen Nutzer.
        var doc = XDocument.Parse(AutostartRegistrar.BuildTaskXml(ExePath));

        var trigger = doc.Root!.Element(Ns + "Triggers")!.Element(Ns + "SessionStateChangeTrigger")!;

        Assert.Equal("true", trigger.Element(Ns + "Enabled")!.Value);
        Assert.Equal("ConsoleConnect", trigger.Element(Ns + "StateChange")!.Value);
        Assert.Null(trigger.Element(Ns + "UserId"));
    }

    [Fact]
    public void BuildTaskXml_AllowsParallelInstances_SoASecondUsersSessionIsNotIgnored()
    {
        // Issue #9: IgnoreNew zählt Instanzen task-weit statt pro Sitzung - solange Nutzer
        // A's Instanz läuft, würde Nutzer B beim Wechsel nie eine eigene bekommen.
        var doc = XDocument.Parse(AutostartRegistrar.BuildTaskXml(ExePath));

        var settings = doc.Root!.Element(Ns + "Settings")!;

        Assert.Equal("Parallel", settings.Element(Ns + "MultipleInstancesPolicy")!.Value);
    }

    [Fact]
    public void BuildTaskXml_DoesNotSkipRunOnBatteryPower()
    {
        // Zweiter Teil desselben Bugfix: schtasks' eigene Standardwerte hätten den Agent auf
        // Notebooks im Akkubetrieb gar nicht erst gestartet bzw. mittendrin gestoppt - für
        // ein Alarmsystem nicht akzeptabel.
        var doc = XDocument.Parse(AutostartRegistrar.BuildTaskXml(@"C:\Program Files\HaelpMi\HaelpMi.Agent.exe"));

        var settings = doc.Root!.Element(Ns + "Settings")!;

        Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
        Assert.Equal("false", settings.Element(Ns + "StopIfGoingOnBatteries")!.Value);
    }

    [Fact]
    public void BuildTaskXml_UsesXmlElementForThePath_SoSpecialCharactersAreEscapedCorrectly()
    {
        // XElement statt String-Interpolation (siehe Kommentar an der Aufrufstelle) - ein
        // Pfad mit "&" darf die erzeugte XML nicht kaputt machen bzw. muss nach dem Parsen
        // unverändert wieder herauskommen.
        const string pathWithAmpersand = @"C:\Program Files\Städtische IT & Verwaltung\HaelpMi.Agent.exe";

        var doc = XDocument.Parse(AutostartRegistrar.BuildTaskXml(pathWithAmpersand));

        var command = doc.Root!.Element(Ns + "Actions")!.Element(Ns + "Exec")!.Element(Ns + "Command")!;
        Assert.Equal(pathWithAmpersand, command.Value);
    }

    private const string ExePath = @"C:\Program Files\HaelpMi\HaelpMi.Agent.exe";

    [Fact]
    public void TaskXmlIsUpToDate_FreshlyBuiltXml_IsConsideredUpToDate()
    {
        // Was EnsureRegistered selbst erzeugt, muss sich auch selbst als "passt schon" erkennen -
        // sonst würde jeder Agent-Start den Task unnötig neu schreiben.
        var xml = AutostartRegistrar.BuildTaskXml(ExePath);

        Assert.True(AutostartRegistrar.TaskXmlIsUpToDate(xml, ExePath));
    }

    [Fact]
    public void TaskXmlIsUpToDate_PreFix_UserIdBoundTask_IsNotUpToDate()
    {
        // Bugfix 11.08.2026: ein Task von VOR dem 08.08.2026-Multi-User-Fix (UserId statt
        // GroupId) muss als veraltet erkannt werden, sonst repariert sich eine alte
        // Installation nie selbst - genau das war der gemeldete "Autostart nach
        // Geräteneustart geht nicht"-Fall.
        const string preFixXml = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>S-1-5-21-1111111111-2222222222-3333333333-1001</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>S-1-5-21-1111111111-2222222222-3333333333-1001</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Actions Context="Author">
                <Exec>
                  <Command>C:\Program Files\HaelpMi\HaelpMi.Agent.exe</Command>
                </Exec>
              </Actions>
            </Task>
            """;

        Assert.False(AutostartRegistrar.TaskXmlIsUpToDate(preFixXml, ExePath));
    }

    [Fact]
    public void TaskXmlIsUpToDate_PreFix_MissingConsoleConnectTrigger_IsNotUpToDate()
    {
        // Bugfix 25.08.2026 (Issue #9): ein Task von VOR diesem Fix hat den GroupId-Principal
        // schon (08.08.2026-Fix), aber weder den ConsoleConnect-Trigger noch Parallel als
        // Instanzrichtlinie - eine bestehende Installation muss sich trotzdem selbst reparieren.
        const string preFixXml = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <GroupId>S-1-5-32-545</GroupId>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>C:\Program Files\HaelpMi\HaelpMi.Agent.exe</Command>
                </Exec>
              </Actions>
            </Task>
            """;

        Assert.False(AutostartRegistrar.TaskXmlIsUpToDate(preFixXml, ExePath));
    }

    [Fact]
    public void TaskXmlIsUpToDate_CommandPointsAtDifferentPath_IsNotUpToDate()
    {
        // Installationsort hat sich geändert (z. B. Reparatur-Installation in einen anderen
        // Ordner) - der alte, jetzt ungültige Pfad im Task darf nicht als "passt schon" gelten.
        var xmlForOldPath = AutostartRegistrar.BuildTaskXml(@"C:\Program Files\HaelpMi-Alt\HaelpMi.Agent.exe");

        Assert.False(AutostartRegistrar.TaskXmlIsUpToDate(xmlForOldPath, ExePath));
    }

    [Fact]
    public void TaskXmlIsUpToDate_UnparsableXml_IsNotUpToDate()
    {
        // Im Zweifel neu registrieren statt eine möglicherweise kaputte Registrierung stehen
        // zu lassen.
        Assert.False(AutostartRegistrar.TaskXmlIsUpToDate("not-xml-at-all", ExePath));
    }
}
