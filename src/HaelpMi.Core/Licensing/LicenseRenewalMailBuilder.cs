namespace HaelpMi.Core.Licensing;

/// <summary>
/// Baut den Text für "Neue Lizenz anfordern" (Issue #20) - bewusst als formelle
/// Verlängerungsanfrage (Nutzungsvereinbarung, nächster Abrechnungszeitraum), nicht als
/// beiläufige Bitte um eine kostenlose neue Lizenz. Die konkrete neue Laufzeit legt der
/// Anbieter fest, der Text fragt nur nach einem Angebot bzw. der verlängerten Lizenz.
/// </summary>
public static class LicenseRenewalMailBuilder
{
    private const string RecipientAddress = "info@it-scholle.de";

    public static LicenseRenewalMailContent Build(Guid customerGroupId, DateTime expiresAtUtc)
    {
        var subject = $"Verlängerungsanfrage – HälpMi-Lizenz {customerGroupId}";
        var body = $"""
            Sehr geehrte Damen und Herren,

            hiermit beantragen wir für unsere HälpMi-Installation die Verlängerung der bestehenden Nutzungsvereinbarung um einen weiteren Abrechnungszeitraum, nahtlos anschließend an das aktuelle Ablaufdatum.

            Kundengruppe: {customerGroupId}
            Aktuelles Ablaufdatum: {expiresAtUtc:dd.MM.yyyy}
            Ansprechpartner: [Name, Telefon/E-Mail]

            Wir bitten um Zusendung eines Angebots bzw. der verlängerten Lizenz.

            Mit freundlichen Grüßen
            """;
        return new LicenseRenewalMailContent(RecipientAddress, subject, body);
    }
}

public sealed record LicenseRenewalMailContent(string To, string Subject, string Body)
{
    public Uri ToMailtoUri() =>
        new($"mailto:{To}?subject={Uri.EscapeDataString(Subject)}&body={Uri.EscapeDataString(Body)}");
}
