using System.Text.Json;
using HaelpMi.Core.Models;

namespace HaelpMi.Core.Storage;

/// <summary>
/// Liest <c>installed-by.json</c> (siehe <see cref="AppPaths.InstalledByInfoFilePath"/>).
/// Anders als <see cref="DeploymentInfoStore"/> bewusst tolerant: eine fehlende oder
/// beschädigte Datei liefert null statt zu werfen, damit ein Gerät ohne diese Datei (z. B.
/// eine vor Issue #10 installierte Bestandsmaschine) nicht die ganze App abbrechen lässt -
/// die Konsequenz ist ausschließlich, dass DashboardAccessGuard dann fail-closed keinen
/// Dashboard-Zugriff gewährt.
/// </summary>
public static class InstalledByInfoStore
{
    public static InstalledByInfo? TryLoad() => TryLoad(AppPaths.InstalledByInfoFilePath);

    public static InstalledByInfo? TryLoad(string path)
    {
        try
        {
            return JsonFileStore.Load<InstalledByInfo>(path);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
