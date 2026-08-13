using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Dünner Wrapper um die Bitwarden-CLI (bw.exe, spricht auch mit selbstgehostetem
/// Vaultwarden) - Nutzerwunsch 13.08.2026: der private Update-Signaturschlüssel soll nicht
/// mehr als Klartextdatei auf der Build-Maschine liegen, sondern verschlüsselt in Vaultwarden,
/// und nur für die Dauer eines Install-Creator-Laufs in den Arbeitsspeicher geladen werden.
///
/// Notizname ist bewusst fest verdrahtet statt einer gespeicherten Einstellung (Nutzerkorrektur
/// 13.08.2026): anders als z. B. ein Installer-Passwort pro Kunde ist der Update-
/// Signaturschlüssel ein einziger, globaler Schlüssel - Schema "HälpMi-&lt;Schlüsseltyp&gt;-&lt;Name&gt;",
/// hier ohne variablen Namensteil, weil es nur diesen einen gibt.
///
/// Passwort/Session-Key laufen ausschließlich über ProcessStartInfo.EnvironmentVariables des
/// jeweils einen bw-Kindprozesses - nie eine dauerhafte Umgebungsvariable, nie geloggt, nie als
/// Kommandozeilen-Argument (das würde in der Prozessliste anderer Nutzer auftauchen).
/// </summary>
public sealed class VaultwardenClient
{
    public const string UpdatePrivateKeyItemName = "HälpMi-Update-PrivateKey";

    private readonly string _bwPath;

    private VaultwardenClient(string bwPath) => _bwPath = bwPath;

    /// <summary>Sucht bw.exe im PATH - landet dort über npm/winget/scoop üblicherweise selbst,
    /// kein Rate-Raten wie bei ISCC.exe (siehe MainWindow.FindIscc) nötig.</summary>
    public static VaultwardenClient? TryCreate()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            foreach (var name in new[] { "bw.exe", "bw.cmd", "bw" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return new VaultwardenClient(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Verbindet mit dem angegebenen Vaultwarden-Server, meldet sich an (falls die CLI noch
    /// keinen gültigen lokalen Login-Zustand hat - das ist Bitwardens eigener, durch das
    /// Master-Passwort verschlüsselter Zustand, kein Zutun unsererseits nötig) und entsperrt
    /// den Tresor. Gibt den Session-Key zurück, der für die restlichen Aufrufe gebraucht wird.
    /// </summary>
    public async Task<VaultwardenUnlockResult> UnlockAsync(string serverUrl, string email, string masterPassword)
    {
        var configResult = await RunAsync(new[] { "config", "server", serverUrl }, env: null);
        if (configResult.ExitCode != 0)
        {
            return VaultwardenUnlockResult.Failure($"Server-Konfiguration fehlgeschlagen: {configResult.StdErr}");
        }

        // "bw login" schlägt fehl, wenn schon eingeloggt - das ist hier kein echter Fehler,
        // sondern der Normalfall bei jedem Start nach dem allerersten (siehe status-Prüfung).
        var status = await GetStatusAsync();
        if (status != "unlocked" && status != "locked")
        {
            var loginEnv = new Dictionary<string, string> { ["BW_PASSWORD"] = masterPassword };
            var loginResult = await RunAsync(new[] { "login", email, "--passwordenv", "BW_PASSWORD" }, loginEnv);
            if (loginResult.ExitCode != 0)
            {
                return VaultwardenUnlockResult.Failure($"Anmeldung fehlgeschlagen: {loginResult.StdErr}");
            }
        }

        var unlockEnv = new Dictionary<string, string> { ["BW_PASSWORD"] = masterPassword };
        var unlockResult = await RunAsync(new[] { "unlock", "--passwordenv", "BW_PASSWORD", "--raw" }, unlockEnv);
        if (unlockResult.ExitCode != 0)
        {
            return VaultwardenUnlockResult.Failure($"Entsperren fehlgeschlagen: {unlockResult.StdErr}");
        }

        var sessionKey = unlockResult.StdOut.Trim();
        return VaultwardenUnlockResult.Success(sessionKey);
    }

    private async Task<string?> GetStatusAsync()
    {
        var result = await RunAsync(new[] { "status" }, env: null);
        if (result.ExitCode != 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            return doc.RootElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Liest den Inhalt der festen Update-Schlüssel-Notiz (Base64-Text, gleiches
    /// Format wie UpdateSigner "genkey" es in privateKeyOut.txt schreibt).</summary>
    public async Task<string?> TryGetUpdatePrivateKeyAsync(string sessionKey)
    {
        var env = new Dictionary<string, string> { ["BW_SESSION"] = sessionKey };
        var syncResult = await RunAsync(new[] { "sync" }, env);
        if (syncResult.ExitCode != 0)
        {
            return null; // best-effort - falls sync fehlschlägt, versuchen wir trotzdem mit dem lokalen Cache-Stand
        }

        var result = await RunAsync(new[] { "get", "notes", UpdatePrivateKeyItemName }, env);
        if (result.ExitCode != 0)
        {
            return null; // nicht gefunden - Aufrufer bietet dann "Neuen Schlüssel erzeugen" an
        }

        var content = result.StdOut.Trim();
        return string.IsNullOrEmpty(content) ? null : content;
    }

    /// <summary>Legt die feste Update-Schlüssel-Notiz neu an. Aufrufer muss vorher selbst
    /// prüfen/warnen, falls schon eine existiert (siehe TryGetUpdatePrivateKeyAsync) - diese
    /// Methode überschreibt bewusst kommentarlos, das Abfangen ist UI-Verantwortung.</summary>
    public async Task<bool> CreateUpdatePrivateKeyNoteAsync(string sessionKey, string privateKeyBase64)
    {
        // Bitwardens "encode"-Schritt (JSON -> Base64) ist Teil des offiziellen CLI-Workflows
        // fürs Anlegen von Items ("bw encode | bw create item") - vermeidet Escaping-Ärger mit
        // Sonderzeichen in der Kommandozeile, die der geheime Inhalt hier gerade NICHT hat
        // (reiner Base64-Text), aber das Notiz-JSON drumherum schon haben könnte.
        var itemJson = JsonSerializer.Serialize(new
        {
            organizationId = (string?)null,
            folderId = (string?)null,
            type = 2, // SecureNote
            name = UpdatePrivateKeyItemName,
            notes = privateKeyBase64,
            secureNote = new { type = 0 },
        });

        var env = new Dictionary<string, string> { ["BW_SESSION"] = sessionKey };
        var encodeResult = await RunAsync(new[] { "encode" }, env, stdin: itemJson);
        if (encodeResult.ExitCode != 0)
        {
            return false;
        }

        var createResult = await RunAsync(new[] { "create", "item", encodeResult.StdOut.Trim() }, env);
        return createResult.ExitCode == 0;
    }

    private async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? env, string? stdin = null)
    {
        var startInfo = new ProcessStartInfo(_bwPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }
        if (env is not null)
        {
            foreach (var (key, value) in env)
            {
                startInfo.EnvironmentVariables[key] = value; // gilt nur für diesen einen Kindprozess, keine dauerhafte Umgebungsvariable
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, await stdOutTask, await stdErrTask);
    }

    private readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr);
}

public sealed record VaultwardenUnlockResult(bool Ok, string? SessionKey, string? Error)
{
    public static VaultwardenUnlockResult Success(string sessionKey) => new(true, sessionKey, null);
    public static VaultwardenUnlockResult Failure(string error) => new(false, null, error);
}
