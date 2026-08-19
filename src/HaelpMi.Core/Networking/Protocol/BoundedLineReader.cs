namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Reads one newline-terminated line from a stream with a hard size cap. Incoming
/// network data (alarm requests, IPC requests) is untrusted (CLAUDE.md security rules):
/// a malformed or hostile sender that never sends '\n' must not make us buffer
/// unbounded memory - <see cref="StreamReader.ReadLineAsync()"/> alone has no such cap.
/// </summary>
internal static class BoundedLineReader
{
    public const int MaxLineBytes = 8 * 1024; // generous for our small JSON messages

    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var oneByte = new byte[1];

        while (buffer.Length < MaxLineBytes)
        {
            var read = await stream.ReadAsync(oneByte, ct);
            if (read == 0)
            {
                return null; // connection closed before a full line arrived
            }

            if (oneByte[0] == (byte)'\n')
            {
                return NetworkSerializer.Encoding.GetString(buffer.ToArray());
            }

            buffer.WriteByte(oneByte[0]);
        }

        return null; // oversized - reject rather than keep buffering
    }
}
