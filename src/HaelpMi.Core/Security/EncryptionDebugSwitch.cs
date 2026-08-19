namespace HaelpMi.Core.Security;

/// <summary>
/// P1-Notfall-Schalter (19.08.2026, Nutzerwunsch): temporäres, klar markiertes Mittel, um
/// die LAN-Verschlüsselung (<see cref="Networking.Protocol.SecureEnvelopeCodec"/>, betrifft
/// Alarm/Ack, Config-Sync-Pull, Audit-Sync, Admin-Rollen-Schlüssel-Sync) auszuschalten, um
/// zu isolieren, ob sie die Ursache für ausbleibende Config-Übertragung ist - NICHT als
/// stille dauerhafte Änderung, sondern ausschließlich über die Umgebungsvariable
/// <c>DISABLE_ENCRYPTION_DEBUG_ONLY=1</c>, die auf jeder Testmaschine einzeln gesetzt werden
/// muss. Bewusst kein Installer-/Config-Schalter - eine Umgebungsvariable ist am schwersten
/// versehentlich mit in einen echten Kundenrechner zu übernehmen.
///
/// Wirkung: alle sieben Stellen, die bisher direkt <c>DeploymentInfo.GroupKeyBase64</c>
/// gelesen haben, lesen jetzt <see cref="Models.DeploymentInfo.EffectiveGroupKeyBase64"/> -
/// mit aktivem Schalter liefert das <c>null</c>, was jeder SecureEnvelope-Aufrufer bereits
/// als "Peer/wir selbst nicht verschlüsselungsfähig" behandelt (siehe SecureEnvelopeCodec-
/// Klassendoku) und dadurch automatisch auf das bestehende Klartextformat zurückfällt - kein
/// zweiter Codepfad nötig.
///
/// WICHTIG (offener Punkt, nicht vergessen): vor jedem Produktiveinsatz, insbesondere dem
/// Sömmerda-Pilot, muss diese Variable auf allen Testmaschinen wieder entfernt UND die
/// Verschlüsselung nachweislich als funktionierend verifiziert werden - dieser Schalter
/// bleibt bis dahin ausschließlich ein Diagnose-Werkzeug für die aktuelle P1-Untersuchung.
/// </summary>
public static class EncryptionDebugSwitch
{
    private const string EnvVarName = "DISABLE_ENCRYPTION_DEBUG_ONLY";

    public static bool IsDisabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvVarName), "1", StringComparison.Ordinal);
}
