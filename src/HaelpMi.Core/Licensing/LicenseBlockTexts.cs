namespace HaelpMi.Core.Licensing;

/// <summary>Hinweistexte je <see cref="LicenseBlockReason"/>, gemeinsam für Toast, Hinweis-Dialog, IPC-Antwort und Dashboard.</summary>
public static class LicenseBlockTexts
{
    public static string Title(LicenseBlockReason reason) => reason switch
    {
        LicenseBlockReason.ManuallyDisabled => "Gerät deaktiviert",
        LicenseBlockReason.NoLicense => "Keine gültige Lizenz",
        LicenseBlockReason.LimitExceeded => "Lizenz ausgeschöpft",
        _ => string.Empty,
    };

    public static string Message(LicenseBlockReason reason) => reason switch
    {
        LicenseBlockReason.ManuallyDisabled =>
            "Ein Admin hat dieses Gerät im Dashboard deaktiviert. Es kann keine Alarme senden oder empfangen, bis ein Admin es dort wieder aktiviert.",
        LicenseBlockReason.NoLicense =>
            "Für dieses Gerät liegt keine gültige Lizenz vor. Es kann keine Alarme senden oder empfangen, bis ein Admin im Dashboard eine Lizenz einspielt.",
        LicenseBlockReason.LimitExceeded =>
            "Mehr Geräte angemeldet, als die Lizenz erlaubt. Dieses Gerät kann keine Alarme senden oder empfangen, bis ein Admin es im Dashboard aktiviert oder eine größere Lizenz einspielt.",
        _ => string.Empty,
    };

    /// <summary>Überschrift des Dashboard-Banners; manuell deaktivierte Geräte erscheinen dort nie.</summary>
    public static string DashboardBannerTitle(LicenseBlockReason reason) =>
        reason == LicenseBlockReason.NoLicense ? "Keine gültige Lizenz - Geräte gesperrt" : "Lizenzkontingent überschritten";
}
