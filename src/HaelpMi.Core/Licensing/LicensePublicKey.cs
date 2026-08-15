namespace HaelpMi.Core.Licensing;

/// <summary>
/// Öffentlicher Ed25519-Schlüssel zur Prüfung von Kunden-Lizenzdateien (FR-32, CLAUDE.md
/// "Lizenz &amp; Secrets": Schlüsselpaar #1, getrennt vom Update-Signaturschlüssel in
/// HaelpMi.Core/Updates/UpdateSignaturePublicKey.cs, niemals verwechseln/zusammenlegen).
/// Nur der öffentliche Teil ist eingebettet - der private Schlüssel entsteht/lebt
/// ausschließlich außerhalb dieses Repos, erzeugt mit
/// <c>HaelpMi.LicenseSigner genkey</c>, benutzt mit <c>HaelpMi.LicenseSigner sign</c>.
///
/// WICHTIG: Der unten eingebettete Wert ist ein Wegwerf-Entwicklungsschlüssel, nur zum
/// Verdrahten/Testen der Verify-Logik - vor jedem echten Release MUSS ein neues Paar
/// erzeugt und hier der neue öffentliche Schlüssel eingetragen werden. Wird dieser
/// Platzhalter jemals in einer echten Kunden-Auslieferung verwendet, akzeptiert das
/// Verify jede mit dem (nur Claude bekannten, nicht mehr sicher aufbewahrten) Dev-
/// Schlüssel signierte Lizenz - kein Sicherheitsgewinn gegenüber "gar keine Signatur".
/// </summary>
public static class LicensePublicKey
{
    private const string Base64 = "V8OBBijOuML0yIuUPsFaIeqiWM5evFSqlWS47ZPkOh4=";

    public static byte[] Bytes { get; } = Convert.FromBase64String(Base64);
}
