using HaelpMi.Core.Updates;

namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// P2P-Verteilung eines signierten Update-Pakets (Abschnitt 11): das Paket selbst kann
/// mehrere zehn MB groß sein (self-contained .NET-Publish-Output) - viel zu groß für die
/// sonst genutzten kleinen JSON-Zeilen (siehe <see cref="BoundedLineReader.MaxLineBytes"/>).
/// Deshalb trägt die Anfrage-Antwort nur Metadaten (Manifest + Länge); das eigentliche
/// Paket folgt als roher, längenpräfixierter Byte-Strom direkt danach auf derselben
/// Verbindung - siehe UpdatePackageDistributionService.
/// </summary>
public sealed record UpdatePullRequestMessage(Guid CustomerGroupId, Guid RequesterDeviceId, string Version);

public sealed record UpdatePullResponseHeader(Guid CustomerGroupId, bool Found, UpdatePackageManifest? Manifest, long PayloadLength);
