using System.Text;
using System.Text.Json;

namespace HaelpMi.Core.Networking.Protocol;

/// <summary>Shared JSON wire format for both the UDP discovery channel and the TCP alarm channel.</summary>
internal static class NetworkSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static byte[] ToUtf8Json<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T? FromUtf8Json<T>(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<T>(bytes, Options);

    /// <summary>TCP framing: one JSON object per line, UTF-8, newline-terminated.</summary>
    public static string ToJsonLine<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

    public static T? FromJsonLine<T>(string line) => JsonSerializer.Deserialize<T>(line, Options);

    public static Encoding Encoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
