namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Lokales Weiterreichen eines bereits über das echte Netzwerk empfangenen und geprüften
/// <see cref="AlarmRequestMessage"/> von der Primary- an eine Satellite-Agent-Instanz
/// derselben Maschine (Issue #9, Fast User Switching). <see cref="SenderAddress"/> als
/// String statt <see cref="System.Net.IPAddress"/>, weil <c>NetworkSerializer</c> dafür
/// keinen eigenen Konverter hat - reiner lokaler Pipe-Transport, keine Notwendigkeit dafür.
/// </summary>
/// <param name="RelayId">Ordnet die <see cref="AlarmRelayReply"/> der Satellites dieser Weiterleitung zu; leer bei älteren Primaries.</param>
public sealed record AlarmRelayMessage(AlarmRequestMessage Request, string SenderAddress, Guid RelayId = default);

/// <summary>
/// Antwort einer Satellite-Instanz an die Primary: Hat diese Sitzung den weitergereichten Alarm angezeigt?
/// Die Primary quittiert dem Sender erst, wenn irgendeine Sitzung der Maschine ihn anzeigt.
/// <see cref="RelayId"/> leer ist die Anmeldung direkt nach dem Verbinden: Dieser Satellite antwortet.
/// </summary>
public sealed record AlarmRelayReply(Guid RelayId, bool Displayed);
