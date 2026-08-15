namespace HaelpMi.Core.Security;

/// <summary>
/// Gemeinsames Zeitfenster für Replay-Schutz bei signierten Behauptungen - lehnt sowohl
/// zu alte (aufgezeichneter, später erneut eingespielter Claim) als auch zu weit in der
/// Zukunft liegende Zeitstempel ab (Uhr falsch gestellt oder manipuliert). 15 Minuten:
/// großzügig genug für normalen Uhren-Drift zwischen zwei Geräten, eng genug um einen
/// alten, abgefangenen Claim nicht dauerhaft gültig zu halten - derselbe Wert, den der
/// Admin-Rollen-Nachweis für genau dasselbe Problem verwendet (siehe dortiger
/// AdminRoleVerifier.MaxAge-Kommentar); hier als gemeinsame Stelle, damit beide
/// Verifikations-Pfade (Admin-Rolle, Geräte-Identität) nicht zweimal leicht
/// unterschiedlich implementiert werden.
/// </summary>
internal static class FreshnessWindow
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    public static bool IsFresh(DateTimeOffset sentAtUtc, DateTimeOffset nowUtc)
    {
        var age = nowUtc - sentAtUtc;
        return age >= TimeSpan.Zero && age <= MaxAge;
    }
}
