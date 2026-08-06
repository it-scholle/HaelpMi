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
        (message.KnownDevices is null || IsPlausible(message.KnownDevices));

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

    public static bool IsPlausible(this AlarmRequestMessage message) =>
        message.CustomerGroupId != Guid.Empty &&
        message.SenderDeviceId != Guid.Empty &&
        message.SenderComputerName.Length <= MaxTextFieldLength &&
        message.SenderUser.Length <= MaxTextFieldLength &&
        message.SenderRoomName.Length <= MaxTextFieldLength &&
        message.SenderRoomNumber.Length <= MaxTextFieldLength &&
        message.Text.Length <= MaxAlarmTextLength;
}
