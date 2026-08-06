namespace HaelpMi.Core.Networking.Protocol;

public enum AlarmFeedbackMessageType
{
    OnMyWay,
    StatusRelay,
}

/// <summary>
/// Wraps whichever of <see cref="AlarmOnMyWayMessage"/> / <see cref="AlarmStatusRelayMessage"/>
/// is being sent over the shared feedback TCP connection (<see cref="AppConstants"/>
/// <c>.AlarmFeedbackTcpPort</c>) - a single explicit discriminator + nested JSON string
/// is simpler and less fragile than relying on System.Text.Json's polymorphic
/// deserialization for two otherwise-unrelated record shapes.
/// </summary>
public sealed record AlarmFeedbackEnvelope(AlarmFeedbackMessageType Type, string PayloadJson);
