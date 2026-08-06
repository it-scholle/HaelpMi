using System.Diagnostics;

namespace HaelpMi.Installer.Tests;

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Startet einen Installer/Deinstaller-Prozess und wartet mit Timeout - nie unbegrenzt blockieren, falls ein Test hängt.</summary>
internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string exePath, string arguments, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exePath, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException($"{Path.GetFileName(exePath)} {arguments} lief länger als {timeout} - vermutlich hängt der Installer auf einer unerwarteten Seite/Rückfrage.");
        }

        return new ProcessResult(process.ExitCode, await stdOutTask, await stdErrTask);
    }

    /// <summary>
    /// Startet einen Installer OHNE zu warten, für Szenarien, die während der laufenden
    /// Installation per Tastatur gesteuert werden müssen (Wizard-Seiten, Rückfrage-Dialoge -
    /// siehe WizardAutomation.cs). Aufrufer ist für das spätere Warten verantwortlich.
    /// </summary>
    public static Process StartInteractive(string exePath, string arguments)
    {
        var psi = new ProcessStartInfo(exePath, arguments) { UseShellExecute = true };
        return Process.Start(psi) ?? throw new InvalidOperationException($"Konnte {exePath} nicht starten.");
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* bereits beendet */ }
    }
}
