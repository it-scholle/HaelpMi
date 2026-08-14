using System.Security.Cryptography;
using System.Text;

namespace HaelpMi.Core.Storage;

/// <summary>
/// Gemeinsame Hash-Berechnung für die Audit-Log-Kette - von <see cref="AuditLog"/>
/// (Schreiben, eigenes Gerät) UND <see cref="AuditIngestStore"/> (Empfangsprüfung,
/// Admin-Seite) genutzt, damit beide exakt dasselbe rechnen. Ohne die Verifikation auf der
/// Empfangsseite würde nur die reine PrevHash-Verkettung geprüft, nicht aber, ob EntryHash
/// tatsächlich zum transportierten Inhalt passt - eine manipulierte, aber in sich
/// konsistent weitergerechnete Fälschung der Kette wäre sonst nicht erkennbar.
/// </summary>
internal static class AuditHashChain
{
    public static string Compute(string prevHash, long seq, DateTimeOffset timestamp, string content)
    {
        var payload = $"{prevHash}|{seq}|{timestamp:O}|{content}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
