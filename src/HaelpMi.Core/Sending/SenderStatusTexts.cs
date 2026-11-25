namespace HaelpMi.Core.Sending;

/// <summary>Texte der Sender-Statusanzeige, die über die reine Zählung hinausgehen.</summary>
public static class SenderStatusTexts
{
    /// <returns>Hinweis mit Lösungsweg, wenn der Alarm an niemanden geht; sonst null.</returns>
    public static string? NoRecipientsHint(int targetCount) =>
        targetCount == 0
            ? "Dieser Alarm erreicht von diesem Gerät aus derzeit kein Gerät. Ein Admin prüft im Dashboard " +
              "unter „Alarm-Profile“ die Empfänger und unter „Geräte / Lizenz“, ob sie installiert und aktiv sind."
            : null;
}
