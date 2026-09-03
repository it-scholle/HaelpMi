namespace HaelpMi.Core.Licensing;

/// <summary>
/// Ein Text pro <see cref="LicenseWarningLevel"/>, geteilt zwischen dem Admin-Dashboard-
/// Banner (Issue #20) und dem Systemstart-Popup (Issue #20-Nacharbeit) - beide zeigen
/// dieselbe Meldung, nur an unterschiedlichen Stellen. Trägt seit der #20-Nacharbeit
/// zusätzlich die <see cref="ProviderContact"/>-Zeile mit (Nutzervorgabe 03.09.2026,
/// ursprünglich schon im #20-Ticket als "mit Kontaktdaten" beschrieben, im ersten
/// Durchgang übersehen).
/// </summary>
public static class LicenseWarningTextFormatter
{
    public static string Format(LicenseWarning warning)
    {
        var message = warning.Level switch
        {
            LicenseWarningLevel.Missing => "Keine Lizenz gefunden. Bitte einen gültigen Lizenzschlüssel einspielen.",
            LicenseWarningLevel.Invalid => "Lizenz ungültig (beschädigt, manipuliert oder für eine andere Installation ausgestellt). Bitte einen gültigen Lizenzschlüssel einspielen.",
            LicenseWarningLevel.Expired => $"Lizenz seit {-warning.DaysRemaining} Tag(en) abgelaufen. Bitte eine neue Lizenz einspielen.",
            LicenseWarningLevel.ExpiringSoon => $"Lizenz läuft in {warning.DaysRemaining} Tag(en) ab. Bitte rechtzeitig eine neue Lizenz einspielen.",
            _ => string.Empty,
        };

        return warning.Level == LicenseWarningLevel.None
            ? message
            : $"{message}{Environment.NewLine}Kontakt: {ProviderContact.DisplayText}";
    }
}
