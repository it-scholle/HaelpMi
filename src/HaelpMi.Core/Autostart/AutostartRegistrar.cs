using System.Diagnostics;
using System.Xml.Linq;

namespace HaelpMi.Core.Autostart;

/// <summary>
/// Self-registers the Agent into Task Scheduler ("bei Anmeldung ausführen", 5.7,
/// FR-nothing-explicit-but-3.1-implies-it): the Agent calls <see cref="EnsureRegistered"/>
/// once on its own startup. Deliberately per-user-session (kein SYSTEM-Dienst), um
/// Session-0-Isolation (5.3) zu vermeiden - ein SYSTEM-Dienst kann keine UI auf dem
/// interaktiven Desktop zeigen, was das erzwungene Popup (FR-9) zwingend braucht.
///
/// Uses schtasks.exe via ProcessStartInfo.ArgumentList (no shell involved, so no
/// command-injection surface) rather than a Task Scheduler COM/NuGet dependency, to
/// keep this self-contained.
/// </summary>
public static class AutostartRegistrar
{
    public const string TaskName = "HaelpMi Agent";

    // Wohlbekannte, sprachunabhängige SID für BUILTIN\Users (S-1-5-32-545) - eine
    // Gruppen-Namens-Angabe wie "Users" wäre auf einem nicht-englischen Windows anders
    // benannt und würde die Registrierung dort lautlos falsch machen.
    private const string BuiltInUsersGroupSid = "S-1-5-32-545";

    public static bool IsRegistered()
    {
        using var process = StartSchtasks(psi =>
        {
            psi.ArgumentList.Add("/Query");
            psi.ArgumentList.Add("/TN");
            psi.ArgumentList.Add(TaskName);
        });

        process.WaitForExit();
        return process.ExitCode == 0;
    }

    /// <summary>Idempotent: safe to call on every Agent startup, not just the very first one.</summary>
    public static bool EnsureRegistered(string executablePath, out string? error)
    {
        error = null;

        if (IsRegistered())
        {
            return true;
        }

        // Bugfix 08.08.2026 (Nutzerfrage "startet die App jetzt beim Boot, egal welcher
        // Nutzer sich einloggt?"): die alte /SC ONLOGON-Registrierung ohne /RU band den
        // Task fest an die SID des Nutzers, der die Registrierung zufällig als Erster
        // ausgelöst hat (live nachgeprüft: <UserId>S-1-...-1001</UserId> in der
        // resultierenden Task-XML) - meldet sich später ein ANDERER Windows-Nutzer auf
        // demselben Gerät an, für den nie zuvor selbst registriert wurde, startet der
        // Agent für den NICHT automatisch. Widerspricht CLAUDE.md ("läuft immer, egal
        // welcher Nutzer angemeldet"). schtasks' Flags (/SC ONLOGON /RU ...) können einen
        // "beliebiger Nutzer, läuft in dessen eigener Sitzung"-Trigger nicht ausdrücken -
        // das braucht einen LogonTrigger OHNE UserId kombiniert mit einem Principal, der
        // auf die Gruppe BUILTIN\Users (statt einen einzelnen Nutzer) zeigt - nur per
        // Task-XML-Import möglich, nicht per einzelnen schtasks-Flags.
        //
        // Zweiter, bei derselben Gelegenheit gefundener Fehler: schtasks' eigene
        // Standardwerte setzen DisallowStartIfOnBatteries/StopIfGoingOnBatteries auf true
        // (live in der Standard-Task-XML bestätigt) - ein Alarmsystem darf nicht davon
        // abhängen, ob ein Notebook gerade am Netzteil hängt. Beides jetzt explizit auf
        // false gesetzt.
        var xmlPath = Path.Combine(Path.GetTempPath(), $"HaelpMiAutostart_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(executablePath), System.Text.Encoding.Unicode);

            using var process = StartSchtasks(psi =>
            {
                psi.ArgumentList.Add("/Create");
                psi.ArgumentList.Add("/TN");
                psi.ArgumentList.Add(TaskName);
                psi.ArgumentList.Add("/XML");
                psi.ArgumentList.Add(xmlPath);
                psi.ArgumentList.Add("/F");
            });

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                return true;
            }

            error = $"Autostart-Registrierung fehlgeschlagen (schtasks Exit {process.ExitCode}): {stderr.Trim()}. " +
                    "Möglicherweise durch Gruppenrichtlinie eingeschränkt (siehe Pflichtenheft Abschnitt 6/8) - " +
                    "in diesem Fall muss der Eintrag per GPO ausgerollt werden.";
            return false;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch (IOException) { /* best-effort - Temp-Datei, kein Beinbruch */ }
        }
    }

    // XElement statt String-Interpolation: escaped executablePath korrekt, falls der Pfad
    // je XML-Sonderzeichen enthalten sollte (theoretisch möglich bei ungewöhnlichen
    // Verzeichnisnamen) - dieselbe Lehre wie "nie roh zusammenbauen, was strukturiert
    // gebaut werden kann" aus den JSON-Stellen in den Installer-Skripten.
    //
    // internal statt private (10.08.2026): direkt per InternalsVisibleTo aus
    // AutostartRegistrarTests abgedeckt, statt den 08.08.2026-Multi-User-Fix nur indirekt
    // über einen echten schtasks.exe-Aufruf zu prüfen (der auf dem Test-/Build-Rechner
    // Rechte/Umgebung voraussetzen würde, die hier nicht garantiert sind).
    internal static string BuildTaskXml(string executablePath)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "HälpMi Agent - startet automatisch bei jeder Anmeldung, unabhängig davon, welcher Nutzer sich anmeldet.")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"))), // keine <UserId> = jede Anmeldung, nicht nur eines bestimmten Nutzers
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(ns + "GroupId", BuiltInUsersGroupSid), // Gruppe statt fester Nutzer-SID - läuft in der jeweils eigenen Sitzung
                        new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"), // Alarmsystem - darf nicht vom Netzteilstatus abhängen
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "true"), // verpasste Anmeldung (Task-Scheduler-Dienst noch nicht bereit) trotzdem nachholen
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    new XElement(ns + "WakeToRun", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"), // kein Zeitlimit - der Agent läuft dauerhaft, keine kurzlebige Aufgabe
                    new XElement(ns + "Priority", "7")),
                new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", executablePath)))));

        return doc.Declaration + Environment.NewLine + doc.Root;
    }

    public static bool TryRemove()
    {
        using var process = StartSchtasks(psi =>
        {
            psi.ArgumentList.Add("/Delete");
            psi.ArgumentList.Add("/TN");
            psi.ArgumentList.Add(TaskName);
            psi.ArgumentList.Add("/F");
        });
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private static Process StartSchtasks(Action<ProcessStartInfo> configureArgs)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        configureArgs(psi);
        return Process.Start(psi) ?? throw new InvalidOperationException("schtasks.exe konnte nicht gestartet werden.");
    }
}
