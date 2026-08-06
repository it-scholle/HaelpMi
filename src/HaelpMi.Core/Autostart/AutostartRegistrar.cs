using System.Diagnostics;

namespace HaelpMi.Core.Autostart;

/// <summary>
/// Self-registers the Agent into the current user's Task Scheduler ("bei Anmeldung
/// ausführen", 5.7, FR-nothing-explicit-but-3.1-implies-it): the Agent calls
/// <see cref="EnsureRegistered"/> once on its own startup. Deliberately per-user
/// (/RL LIMITED, no /RU) rather than a real Windows service, to avoid Session-0
/// isolation (5.3) - a SYSTEM service cannot show UI on the interactive user's desktop,
/// which the forced popup (FR-9) fundamentally requires.
///
/// Uses schtasks.exe via ProcessStartInfo.ArgumentList (no shell involved, so no
/// command-injection surface) rather than a Task Scheduler COM/NuGet dependency, to
/// keep this self-contained.
/// </summary>
public static class AutostartRegistrar
{
    public const string TaskName = "HaelpMi Agent";

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

        using var process = StartSchtasks(psi =>
        {
            psi.ArgumentList.Add("/Create");
            psi.ArgumentList.Add("/TN");
            psi.ArgumentList.Add(TaskName);
            psi.ArgumentList.Add("/TR");
            psi.ArgumentList.Add($"\"{executablePath}\"");
            psi.ArgumentList.Add("/SC");
            psi.ArgumentList.Add("ONLOGON");
            psi.ArgumentList.Add("/RL");
            psi.ArgumentList.Add("LIMITED");
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
