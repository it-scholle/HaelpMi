namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Sanity checks for untrusted incoming data (CLAUDE.md security rules: broadcast
/// replies and alarm requests are never assumed well-formed just because they
/// deserialized). None of this is an authentication/authorization mechanism - there is
/// still no central permission check on *who* may send what (that principle survives
/// Version 2's admin-role update unchanged, see CLAUDE.md "Weiterhin gültig") - it only
/// rejects obviously bogus or abusive payloads (empty device ids, absurdly long
/// strings, invalid ports, invalid enum values).
/// </summary>
internal static class MessageValidation
{
    private const int MaxTextFieldLength = 200;
    private const int MaxAlarmTextLength = 500;
    private const int MaxVersionStringLength = 40;

    // LAN-Verschlüsselung (siehe SecureEnvelopeCodec): Ed25519-Public-Keys sind fix 32
    // Byte -> 44 Zeichen Base64, hier großzügig aufgerundet. Nonce ist fix 12 Byte ->
    // 16 Zeichen Base64. Ciphertext-Obergrenze orientiert sich an
    // BoundedLineReader.MaxLineBytes (64 KB TCP-Zeilenlimit) abzüglich JSON-Hülle/
    // Base64-Overhead - großzügig, aber endlich, wie MaxKnownDevicesCount unten. Die
    // enthaltene Signatur wird hier NICHT separat geprüft - sie liegt innerhalb des
    // verschlüsselten Klartexts und ist vor dem Entschlüsseln unsichtbar, die eigentliche
    // Sicherheitsgrenze ist ohnehin die Krypto-Prüfung in SecureEnvelopeCodec, nicht diese
    // Plausibilitätsschranken (siehe Klassendoku oben).
    private const int MaxPublicKeyBase64Length = 64;
    private const int MaxNonceBase64Length = 24;
    private const int MaxCiphertextBase64Length = 48 * 1024;

    // Nutzerwunsch 05.08.2026 (Gossip-Anhang): eine sehr großzügige, aber endliche Grenze -
    // der eigentliche Schutz gegen ein überdimensioniertes Datagramm ist schon
    // DiscoveryService.MaxDatagramBytes (das Paket würde vorher verworfen); das hier ist nur
    // die zusätzliche Plausibilitätsprüfung auf bereits erfolgreich geparste Daten.
    private const int MaxKnownDevicesCount = 1000;

    public static bool IsPlausible(this BootCallMessage message) =>
        message.CustomerGroupId != Guid.Empty &&
        message.DeviceId != Guid.Empty &&
        message.ComputerName.Length <= MaxTextFieldLength &&
        message.User.Length <= MaxTextFieldLength &&
        message.RoomName.Length <= MaxTextFieldLength &&
        message.RoomNumber.Length <= MaxTextFieldLength &&
        message.TcpPort is > 0 and <= 65535 &&
        message.ProgramVersion.Length <= MaxVersionStringLength &&
        Enum.IsDefined(message.Role) &&
        Enum.IsDefined(message.Kind) &&
        (message.KnownDevices is null || IsPlausible(message.KnownDevices)) &&
        (message.DeviceIdentityPublicKeyBase64 is null || message.DeviceIdentityPublicKeyBase64.Length <= MaxPublicKeyBase64Length);

    private static bool IsPlausible(IReadOnlyList<KnownDeviceSummary> knownDevices) =>
        knownDevices.Count <= MaxKnownDevicesCount &&
        knownDevices.All(d =>
            d.DeviceId != Guid.Empty &&
            d.ComputerName.Length <= MaxTextFieldLength &&
            d.User.Length <= MaxTextFieldLength &&
            d.RoomName.Length <= MaxTextFieldLength &&
            d.RoomNumber.Length <= MaxTextFieldLength &&
            d.IpAddress.Length <= MaxTextFieldLength &&
            d.TcpPort is > 0 and <= 65535 &&
            Enum.IsDefined(d.Role));

    public static bool IsPlausible(this SecureEnvelope envelope) =>
        envelope.CustomerGroupId != Guid.Empty &&
        envelope.DeviceId != Guid.Empty &&
        envelope.NonceBase64.Length <= MaxNonceBase64Length &&
        envelope.CiphertextBase64.Length <= MaxCiphertextBase64Length;

    public static bool IsPlausible(this AlarmRequestMessage message) =>
        message.CustomerGroupId != Guid.Empty &&
        message.SenderDeviceId != Guid.Empty &&
        message.SenderComputerName.Length <= MaxTextFieldLength &&
        message.SenderUser.Length <= MaxTextFieldLength &&
        message.SenderRoomName.Length <= MaxTextFieldLength &&
        message.SenderRoomNumber.Length <= MaxTextFieldLength &&
        message.Text.Length <= MaxAlarmTextLength;
}
