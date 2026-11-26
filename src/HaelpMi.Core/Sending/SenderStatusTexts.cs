namespace HaelpMi.Core.Sending;

/// <summary>Texte der Sender-Statusanzeige, die über die reine Zählung hinausgehen.</summary>
public static class SenderStatusTexts
{
    public const string NoRecipientsHeadline = "Kein Empfänger eingestellt!";

    /// <summary>Titel des neutralen Windows-Hinweises nach einem Alarm ohne erreichbare Empfänger.</summary>
    public const string MaintenanceHintTitle = "Wartung erforderlich";

    /// <summary>Bewusst ohne Produktnamen und ohne Alarmbezug, damit Umstehende den stillen Alarm nicht erkennen.</summary>
    public const string MaintenanceHintText =
        "Ein Hintergrunddienst konnte eine Meldung nicht zustellen (Code H-00). Bitte die IT informieren.";

    // Ohne Empfänger steht die Abbruchmeldung von Anfang an da: Der Sender soll das Popup nicht
    // mit einem normal laufenden Alarm verwechseln.
    public static string Headline(bool stillSending, int targetCount) =>
        targetCount == 0 ? NoRecipientsHeadline
        : stillSending ? "Alarm wird gesendet..."
        : "Alarm beendet";

    /// <returns>Hinweis mit Lösungsweg, wenn der Alarm an niemanden geht; sonst null.</returns>
    public static string? NoRecipientsHint(int targetCount) =>
        targetCount == 0
            ? "Info: Ein Admin muss im Dashboard Empfänger für dieses Profil einrichten."
            : null;
}
