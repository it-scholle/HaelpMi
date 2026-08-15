namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Äußere, für jeden verschlüsselten Kanal identische Hülle um eine Ende-zu-Ende
/// verschlüsselte und geräte-signierte Nachricht (Versiegeln/Öffnen siehe
/// SecureEnvelopeCodec). CustomerGroupId bleibt im Klartext sichtbar - unverändertes,
/// schnelles Früh-Filter-Gate wie bisher (CustomerGroupFilter), genau wie DeviceId, das
/// der Empfänger braucht, um VOR der Entschlüsselung den passenden gepinnten
/// öffentlichen Geräte-Schlüssel nachzuschlagen. V ist die Envelope-Formatversion
/// (aktuell immer 1) - bewusst getrennt von BootCallMessage.ProtocolVersion, das die
/// Verschlüsselungsfähigkeit eines PEERS beschreibt, nicht das Nachrichtenformat selbst.
/// </summary>
public sealed record SecureEnvelope(
    int V,
    Guid CustomerGroupId,
    Guid DeviceId,
    string NonceBase64,
    string CiphertextBase64);
