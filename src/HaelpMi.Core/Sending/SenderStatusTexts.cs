namespace HaelpMi.Core.Sending;

/// <summary>Texte der Sender-Statusanzeige, die über die reine Zählung hinausgehen.</summary>
public static class SenderStatusTexts
{
    /// <returns>Hinweis mit Lösungsweg, wenn der Alarm an niemanden geht; sonst null.</returns>
    public static string? NoRecipientsHint(int targetCount) =>
        targetCount == 0
            ? "Für dieses Gerät sind in diesem Alarm-Profil keine Empfänger eingerichtet. " +
              "Ein Admin ordnet sie im Dashboard unter „Alarm-Profile“ zu."
            : null;
}
