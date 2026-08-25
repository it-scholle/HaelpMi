namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Lokales Weiterreichen eines bereits über das echte Netzwerk empfangenen und geprüften
/// <see cref="AlarmRequestMessage"/> von der Primary- an eine Satellite-Agent-Instanz
/// derselben Maschine (Issue #9, Fast User Switching). <see cref="SenderAddress"/> als
/// String statt <see cref="System.Net.IPAddress"/>, weil <c>NetworkSerializer</c> dafür
/// keinen eigenen Konverter hat - reiner lokaler Pipe-Transport, keine Notwendigkeit dafür.
/// </summary>
public sealed record AlarmRelayMessage(AlarmRequestMessage Request, string SenderAddress);
