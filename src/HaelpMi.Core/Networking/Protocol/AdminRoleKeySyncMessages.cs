namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Migrationspfad-Schlüsselaustausch zwischen zwei Admin-Geräten (siehe
/// Security.AdminRoleTrustStore-Klassendoku) - ausgelöst bei jedem direkten
/// Admin&lt;-&gt;Admin-Boot-Call-Kontakt (<see cref="Networking.DiscoveryService.
/// AdminPeerContactObserved"/>, symmetrisch: beide Seiten fragen unabhängig voneinander
/// beim jeweils anderen an). <see cref="OwnPublicKeyBase64"/>/<see cref="IsReplaceable"/>
/// beschreiben, was der Anfragende selbst schon hat - der Antwortende entscheidet daran,
/// ob er seinen eigenen Schlüssel zurückgibt (Anfragender hat noch keinen, oder dessen
/// ersetzbarer Schlüssel ist lexikografisch größer als der eigene). Braucht der
/// Antwortende stattdessen SELBST den Schlüssel des Anfragenden, tut diese eine Antwort
/// nichts - die symmetrische Gegenrichtung (der Antwortende fragt seinerseits beim selben
/// Peer an) übernimmt das, ausgelöst vom selben Boot-Call-Austausch.
/// </summary>
public sealed record AdminRoleKeySyncRequestMessage(
    Guid CustomerGroupId,
    Guid RequesterDeviceId,
    string? OwnPublicKeyBase64,
    bool IsReplaceable);

/// <summary>
/// Antwort auf <see cref="AdminRoleKeySyncRequestMessage"/> - NUR über einen geöffneten
/// <see cref="SecureEnvelope"/> aussagekräftig: <see cref="PrivateKeyBase64"/> wird nie
/// im Klartext verschickt (siehe HandleIncomingKeySyncRequestAsync), fehlt der
/// verschlüsselte Kanal (kein Gruppenschlüssel/keine gepinnte Geräte-Identität), bleibt
/// die Antwort leer (beide Felder <c>null</c>) statt den Schlüssel unverschlüsselt
/// preiszugeben.
/// </summary>
public sealed record AdminRoleKeySyncResponseMessage(
    Guid CustomerGroupId,
    string? PublicKeyBase64,
    string? PrivateKeyBase64);
