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
}
