using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Einziger Einstiegspunkt für "Lizenzdatei laden und prüfen" - hier hängt strukturell die
/// Fail-open-Garantie dran: fehlende Datei, kaputtes JSON, IO-Fehler oder eine ungültige
/// Signatur führen alle nur zu null, nie zu einer Ausnahme, die irgendeinen Aufrufer
/// (insbesondere den alarmkritischen Pfad) stören könnte. Bewusst catch (Exception), nicht
/// enger - das Ziel ist "kann diese Methode niemals werfen", nicht "kennt alle denkbaren
/// Fehlerarten".
/// </summary>
public static class LicenseFileLoader
{
    public static LicenseFile? LoadAndVerify()
    {
        try
        {
            var file = JsonFileStore.Load<LicenseFile>(AppPaths.LicenseFilePath);
            return file is not null && LicenseVerifier.Verify(file) ? file : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
