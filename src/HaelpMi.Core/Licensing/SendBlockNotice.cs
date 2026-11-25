namespace HaelpMi.Core.Licensing;

/// <summary>Hinweis für ein Gerät, das gerade keinen Alarm senden darf.</summary>
public sealed record SendBlockNotice(string Title, string Message)
{
    /// <returns>null, wenn das Gerät senden darf; sonst der Hinweis, der statt des Alarms erscheint.</returns>
    public static SendBlockNotice? For(bool deviceRemoved, LicenseBlockReason reason)
    {
        if (deviceRemoved)
        {
            return new SendBlockNotice(
                "Gerät entfernt",
                "Dieses Gerät wurde von einem Admin im Dashboard gelöscht und kann keine Alarme mehr senden. " +
                "HälpMi kann deinstalliert werden - siehe den separaten Hinweis dazu.");
        }

        return reason == LicenseBlockReason.None
            ? null
            : new SendBlockNotice(LicenseBlockTexts.Title(reason), LicenseBlockTexts.Message(reason));
    }
}
