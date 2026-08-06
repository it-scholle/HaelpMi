namespace HaelpMi.Core.Updates;

/// <summary>
/// Öffentlicher Ed25519-Schlüssel zur Prüfung signierter Programm-Updates (Abschnitt 11,
/// CLAUDE.md "Lizenz &amp; Secrets": separater Schlüssel von der Kunden-Lizenzsignatur).
/// Nur der öffentliche Teil ist eingebettet - der private Schlüssel entsteht/lebt
/// ausschließlich außerhalb dieses Repos, erzeugt mit
/// <c>HaelpMi.UpdateSigner genkey</c>, benutzt mit <c>HaelpMi.UpdateSigner sign</c>.
///
/// WICHTIG: Der unten eingebettete Wert ist ein Wegwerf-Entwicklungsschlüssel, nur zum
/// Verdrahten/Testen der Verify-Logik - vor jedem echten Release MUSS ein neues Paar
/// erzeugt und hier der neue öffentliche Schlüssel eingetragen werden. Wird dieser
/// Platzhalter jemals in einer echten Kunden-Auslieferung verwendet, akzeptiert das
/// Verify jedes mit dem (nur Claude bekannten, nicht mehr sicher aufbewahrten) Dev-
/// Schlüssel signierte Paket - kein Sicherheitsgewinn gegenüber "gar keine Signatur".
/// </summary>
public static class UpdateSignaturePublicKey
{
    private const string Base64 = "QNcR2rgL1EqRk8Y+c78xFrLEQW0cNZfNY0kH/AN9Rv0=";

    public static byte[] Bytes { get; } = Convert.FromBase64String(Base64);
}
