using System.Text;

namespace HaelpMi.Core.Security;

/// <summary>
/// Baut die exakte Byte-Nutzlast, die für eine Admin-Rollen-Behauptung signiert/geprüft
/// wird - von <see cref="AdminRoleSigner"/> UND <see cref="AdminRoleVerifier"/> genutzt,
/// damit beide Seiten garantiert dasselbe rechnen (kein Drift), gleiches Prinzip wie
/// <c>AuditHashChain</c> beim Audit-Log. Bewusst nur drei Felder: <c>CustomerGroupId</c>
/// (verhindert, dass eine für einen anderen Kreis gültige Signatur hier akzeptiert würde -
/// zusätzlich zur ohnehin vorgeschalteten <c>CustomerGroupFilter</c>-Prüfung),
/// <c>DeviceId</c> (bindet die Behauptung an genau dieses Gerät, keine Übertragbarkeit auf
/// ein anderes) und <c>SentAtUtc</c> (Replay-Schutz über ein Freshness-Fenster, siehe
/// <see cref="AdminRoleVerifier.MaxAge"/>).
/// </summary>
internal static class AdminRoleClaim
{
    public static byte[] BuildPayload(Guid customerGroupId, Guid deviceId, DateTimeOffset sentAtUtc)
    {
        var text = $"{customerGroupId:D}|{deviceId:D}|{sentAtUtc:O}";
        return Encoding.UTF8.GetBytes(text);
    }
}
