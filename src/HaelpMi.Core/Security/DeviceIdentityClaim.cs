using System.Security.Cryptography;
using System.Text;

namespace HaelpMi.Core.Security;

/// <summary>
/// Baut die exakte Byte-Nutzlast, die für die Geräte-Identitätssignatur eines
/// SecureEnvelope-Inhalts signiert/geprüft wird (siehe SecureEnvelopeCodec) - von
/// DeviceIdentitySigner UND DeviceIdentityVerifier genutzt, damit beide Seiten
/// garantiert dasselbe rechnen (kein Drift), gleiches Prinzip wie AdminRoleClaim für den
/// Admin-Rollen-Nachweis (siehe dortige Klassendoku). Der Nachrichteninhalt selbst geht
/// nur als SHA-256-Hash ein (nicht die vollen Bytes), damit diese Payload unabhängig von
/// der Nachrichtengröße kompakt bleibt. CustomerGroupId + DeviceId + Nonce binden die
/// Signatur an genau diesen einen Envelope (keine Wiederverwendung in einem anderen
/// Kreis, für ein anderes Gerät oder mit einem anderen Nonce), SentAtUtc trägt den
/// Replay-Schutz (siehe FreshnessWindow).
/// </summary>
internal static class DeviceIdentityClaim
{
    public static byte[] BuildPayload(Guid customerGroupId, Guid deviceId, byte[] nonce, byte[] bodyJsonBytes, DateTimeOffset sentAtUtc)
    {
        var bodyHashBase64 = Convert.ToBase64String(SHA256.HashData(bodyJsonBytes));
        var text = $"{customerGroupId:D}|{deviceId:D}|{Convert.ToBase64String(nonce)}|{bodyHashBase64}|{sentAtUtc:O}";
        return Encoding.UTF8.GetBytes(text);
    }
}
