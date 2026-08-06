namespace HaelpMi.Core.Ipc;

/// <summary>
/// Local same-machine actions Config asks the always-running Agent to perform,
/// because the Agent owns the actual network sockets (5.2). Ping just checks
/// reachability, e.g. before enabling buttons that need it.
/// </summary>
public enum IpcCommandType
{
    Ping,
    Rebroadcast,
    SearchAgain,
    SelfTest,
}

public sealed record IpcRequest(IpcCommandType Command);

public sealed record IpcResponse(bool Success, string? Error = null);
