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
}
