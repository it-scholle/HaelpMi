namespace HaelpMi.Core.Licensing;

/// <summary>
/// Issue #94: entscheidet, ob eine neu eingespielte Lizenz gegenüber der aktuell aktiven
/// ein Downgrade ist - ausschließlich anhand der Anzahl Gerätelizenzen
/// (<see cref="License.UserLimit"/>), Tier-Name und Ablaufdatum spielen dafür keine Rolle
/// (Nutzerentscheidung 08.09.2026). <c>null</c> steht für unbegrenzt (Tier XL) und zählt
/// dabei als größer als jede konkrete Zahl.
/// </summary>
public static class LicensePackageComparer
{
    public static bool IsDowngrade(License candidate, License current)
    {
        if (current.UserLimit is null)
        {
            return candidate.UserLimit is not null;
        }

        return candidate.UserLimit is { } candidateLimit && candidateLimit < current.UserLimit.Value;
    }
}
