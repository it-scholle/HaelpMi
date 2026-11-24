namespace HaelpMi.Core.Sending;

/// <summary>
/// Issue #136 Fixvorschlag 5: generische Wiederholungs-Schleife für ein Best-Effort-Signal,
/// das sein Ziel zuverlässiger erreichen soll (hier: das abschließende "Sender pingt nicht
/// mehr"-Relay, siehe <see cref="RepeatingAlarmSession"/>) - als eigene, netzwerkfreie
/// Klasse, damit die Schleife selbst (Anzahl, Abstand) ohne TCP/WPF testbar ist.
/// </summary>
internal static class RelayBurstSender
{
    /// <param name="sendOnce">Eine Sendung - wird (<paramref name="totalCount"/> - 1)-mal aufgerufen, im Abstand von <paramref name="spacing"/>.</param>
    /// <param name="totalCount">Gesamtzahl der Sendungen inklusive der bereits vorher einmal gesendeten ersten.</param>
    public static async Task SendRemainingAsync(Func<Task> sendOnce, int totalCount, TimeSpan spacing)
    {
        for (var i = 1; i < totalCount; i++)
        {
            await Task.Delay(spacing);
            await sendOnce();
        }
    }
}
