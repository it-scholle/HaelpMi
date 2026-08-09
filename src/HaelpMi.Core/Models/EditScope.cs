namespace HaelpMi.Core.Models;

/// <summary>
/// Was der exklusive Edit-Lock (CLAUDE.md, Abschnitt 5) sperrt (Nutzer-Klarstellung
/// 04.08.2026): nicht mehr ein Verwaltungsbereich ("Kreis"), sondern genau der Datensatz,
/// den ein Admin gerade bearbeitet - eine Gruppe oder ein Alarm-Profil. "Kreis" war von
/// Anfang an der Begriff für die kundennummernbasierte Netz-Isolation (siehe
/// CustomerGroupId/CustomerGroupFilter), nie ein Dashboard-Objekt.
/// </summary>
public enum EditScopeKind
{
    Group,
    Profile,

    /// <summary>
    /// Nutzerwunsch 09.08.2026: Freigabe-Klick für Programm-Updates
    /// (<see cref="SharedConfig.UpdateRollout"/>) bekommt denselben exklusiven Edit-Lock
    /// wie Gruppe/Alarm-Profil - es gibt aber immer genau EINEN Datensatz dieser Art
    /// (kundengruppenweit, nicht pro Gerät), deshalb mit einer fest verdrahteten ScopeId
    /// (<see cref="AppConstants.UpdateRolloutScopeId"/>) statt einer echten Datensatz-Id.
    /// </summary>
    UpdateRollout,
}
