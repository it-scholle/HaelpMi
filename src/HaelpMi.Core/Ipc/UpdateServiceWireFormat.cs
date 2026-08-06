using System.Text;
using System.Text.Json;

namespace HaelpMi.Core.Ipc;

/// <summary>
/// Öffentliches Gegenstück zu Networking/Protocol/NetworkSerializer (das bewusst
/// <c>internal</c> bleibt) - HaelpMi.UpdateService liegt außerhalb von HaelpMi.Core und
/// braucht daher einen eigenen, öffentlichen Zugang zum selben JSON-Zeilenformat, statt
/// über eine pauschale InternalsVisibleTo Zugriff auf alle Core-Interna zu bekommen.
/// Dieselben JsonSerializerOptions wie NetworkSerializer, damit beide Seiten des Named
/// Pipes garantiert dasselbe Wire-Format sprechen.
/// </summary>
public static class UpdateServiceWireFormat
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string ToJsonLine<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

    public static T? FromJsonLine<T>(string line) => JsonSerializer.Deserialize<T>(line, Options);
}
