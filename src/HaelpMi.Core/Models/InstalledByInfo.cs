namespace HaelpMi.Core.Models;

/// <summary>
/// Vom Installer einmalig beim tatsächlichen Setup-Lauf auf der Kundenmaschine geschrieben
/// (<c>installed-by.json</c>, siehe installer/HaelpMiCommon.iss.inc, WriteInstalledByJson) -
/// anders als <see cref="DeploymentInfo"/> (vom Install-Creator beim Bauen des Installers
/// erzeugt) ist der installierende Windows-Nutzer erst zur Installationszeit bekannt.
/// Grundlage für Issue #10: das Admin-Dashboard darf nur bei diesem Windows-Nutzer
/// sichtbar/startbar sein.
/// </summary>
public sealed class InstalledByInfo
{
    public required string InstallingUserName { get; set; }
}
