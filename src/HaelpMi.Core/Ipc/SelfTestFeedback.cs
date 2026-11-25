using HaelpMi.Core.Licensing;

namespace HaelpMi.Core.Ipc;

/// <summary>Ein abgelehnter oder gescheiterter Selbsttest, wie ihn das Einstellungsfenster anzeigt.</summary>
/// <param name="OfferDashboard">true, wenn sich die Ursache im Admin-Dashboard beheben lässt und dieses Gerät es öffnen darf.</param>
public sealed record SelfTestFailure(string Message, bool OfferDashboard);

/// <summary>Gründe, aus denen der Agent einen Selbsttest (FR-27) nicht ausführt, jeweils mit Lösungsweg.</summary>
public static class SelfTestFeedback
{
    public const string NoProfile =
        "Es gibt noch kein Alarm-Profil, der Selbsttest braucht eines. Ein Admin legt es im Admin-Dashboard im Reiter „Alarm-Profile“ an.";

    public const string DeviceRemoved =
        "Dieses Gerät wurde im Admin-Dashboard gelöscht und kann keine Alarme mehr senden. Um es wieder einzubinden, HälpMi auf diesem Gerät neu installieren.";

    public const string NotReceived =
        "Der Testalarm ist auf diesem Gerät nicht angekommen. Bitte die HälpMi-Einstellungen schließen und neu öffnen. " +
        "Hilft das nicht, prüfen, ob die Windows-Firewall HälpMi blockiert, und Windows neu anmelden.";

    public const string OfferDashboardQuestion = "Admin-Dashboard jetzt öffnen?";

    /// <returns>null, wenn der Selbsttest läuft; sonst die Meldung für den Nutzer.</returns>
    public static SelfTestFailure? Evaluate(IpcResponse response, bool canOpenDashboard)
    {
        if (response.Success)
        {
            return null;
        }

        var message = string.IsNullOrWhiteSpace(response.Error) ? NotReceived : response.Error;
        return new SelfTestFailure(message, canOpenDashboard && IsFixableInDashboard(message));
    }

    private static bool IsFixableInDashboard(string message) =>
        message == NoProfile
        || Enum.GetValues<LicenseBlockReason>().Any(reason => reason != LicenseBlockReason.None && LicenseBlockTexts.Message(reason) == message);
}
