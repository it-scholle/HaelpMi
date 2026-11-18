using System.Diagnostics;
using System.Text;

namespace HaelpMi.E2E.Tests;

internal sealed record ProzessErgebnis(int ExitCode, string StdOut);

internal static class Prozess
{
    public static ProzessErgebnis Ausfuehren(string exe, string argumente, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo(exe, argumente)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"{exe} ließ sich nicht starten.");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeout ?? TimeSpan.FromSeconds(60)))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"{exe} {argumente} hat sich nicht rechtzeitig beendet.");
        }

        return new ProzessErgebnis(p.ExitCode, stdout.Result + stderr.Result);
    }

    /// <summary>Per -EncodedCommand, damit Umlaute (z. B. "HälpMi Alarm") unabhängig von der Konsolen-Codepage ankommen.</summary>
    public static string PowerShell(string skript)
    {
        var kodiert = Convert.ToBase64String(Encoding.Unicode.GetBytes(skript));
        var ergebnis = Ausfuehren("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + kodiert);
        if (ergebnis.ExitCode != 0)
        {
            throw new InvalidOperationException($"PowerShell-Aufruf fehlgeschlagen ({ergebnis.ExitCode}): {ergebnis.StdOut}");
        }

        return ergebnis.StdOut.Trim();
    }

    /// <summary>
    /// Startet über die Shell statt CreateProcess: die Inno-Installer verlangen per Manifest
    /// Adminrechte, CreateProcess scheitert daran (Fehler 740), ShellExecute erhöht über UAC.
    /// </summary>
    public static async Task<int> UeberShellStartenUndWartenAsync(string exe, string argumente, TimeSpan timeout)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, argumente) { UseShellExecute = true })
            ?? throw new InvalidOperationException($"{exe} ließ sich nicht starten.");
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"{Path.GetFileName(exe)} lief länger als {timeout}.");
        }

        return p.ExitCode;
    }

    public static int EigeneSitzung { get; } = Process.GetCurrentProcess().SessionId;

    public static bool LaeuftInEigenerSitzung(string prozessName)
    {
        var prozesse = Process.GetProcessesByName(prozessName);
        var gefunden = prozesse.Any(p => p.SessionId == EigeneSitzung);
        foreach (var p in prozesse) p.Dispose();
        return gefunden;
    }

    public static void Beenden(string prozessName)
    {
        foreach (var p in Process.GetProcessesByName(prozessName))
        {
            try
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Bereits beendet oder fremde Sitzung.
            }
            finally
            {
                p.Dispose();
            }
        }
    }
}
