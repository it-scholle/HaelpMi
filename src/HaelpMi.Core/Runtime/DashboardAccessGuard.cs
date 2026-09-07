using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Runtime;

/// <summary>
/// Issue #10: das Admin-Dashboard darf ausschließlich bei dem Windows-Nutzer sichtbar/
/// startbar sein, der die Installation durchgeführt hat (<c>installed-by.json</c>, vom
/// Installer zur Installationszeit geschrieben) - unabhängig von Fast User Switching auf
/// derselben Maschine (Issue #9 lässt den Agent-Prozess bewusst für jede Windows-Sitzung
/// laufen, betrifft aber nicht das Dashboard). Reiner Namens- statt SID-Abgleich (CLAUDE.md
/// "Kein Over-Engineering") - für den beschriebenen Einzelmaschinen-LAN-Einsatz mit i. d. R.
/// lokalen Konten ausreichend.
/// </summary>
public static class DashboardAccessGuard
{
    public static bool CurrentUserMayOpenDashboard(Role role) =>
        CurrentUserMayOpenDashboard(role, InstalledByInfoStore.TryLoad()?.InstallingUserName, Environment.UserName);

    /// <summary>Testbarer Kern ohne Dateizugriff/Environment-Abhängigkeit.</summary>
    public static bool CurrentUserMayOpenDashboard(Role role, string? installingUserName, string currentUserName)
    {
        if (role != Role.Admin)
        {
            return false;
        }

        // Fehlt installed-by.json (z. B. eine vor Issue #10 installierte Bestandsmaschine),
        // fail-closed statt fail-open - Dashboard-Sichtbarkeit bei Fremdnutzern ist laut
        // Issue #10 sicherheits-/datenschutzrelevant, kein reines UI-Detail.
        return installingUserName is not null
            && string.Equals(installingUserName, currentUserName, StringComparison.OrdinalIgnoreCase);
    }
}
