namespace HaelpMi.InstallCreator;

/// <summary>
/// **Regel (Nutzerentscheidung 01.09.2026): jede Zwischenablage-Kopie in diesem Projekt läuft
/// über <see cref="TryCopy"/>, nie über einen direkten Clipboard-Aufruf.** Grund, ausführlich
/// hier statt an jeder Aufrufstelle wiederholt (siehe ursprüngliche Fehlerbericht-Kette vom
/// 06./11.08.2026 zum Passwort-Kopieren-Knopf für die volle Vorgeschichte):
///
/// System.Windows.Clipboard (WPF-eigen) wirft auf einem STA-Thread ohne eigene
/// Windows-Nachrichtenschleife zuverlässig eine Exception, obwohl der Schreibvorgang auf
/// OS-Ebene tatsächlich ankommt - "funktioniert, meldet aber fälschlich einen Fehler".
/// System.Windows.Forms.Clipboard.SetDataObject hat einen offiziell dafür vorgesehenen
/// retryTimes/retryDelay-Parameter für CLIPBRD_E_CANT_OPEN und läuft zuverlässig direkt auf
/// dem WPF-UI-Thread, meldet aber ebenfalls gelegentlich fälschlich CLIPBRD_E_CANT_OPEN aus
/// seinem eigenen internen Render-/Flush-Schritt, obwohl der eigentliche Schreibvorgang
/// längst angekommen ist - deshalb das Rücklese-Verify vor jedem "wirklich fehlgeschlagen".
/// Ohne all das kann ein Zwischenablage-Zugriff das ganze Programm hängen lassen oder mit
/// einer Falschmeldung abstürzen, wenn ein anderer Prozess (z. B. VM-Zwischenablage-
/// Synchronisation) die Zwischenablage kurz blockiert.
/// </summary>
internal static class ClipboardCopier
{
    /// <summary>
    /// Blockiert die UI im schlechtesten Fall knapp 9s statt 4s - für einen manuell
    /// angestoßenen Klick hinnehmbar, ein Zwischenablage-Zugriff lässt sich nicht sinnvoll
    /// in einen Hintergrund-Task auslagern (die Zwischenablage-API selbst verlangt den
    /// UI-Thread).
    /// </summary>
    public static bool TryCopy(string text, out int lastErrorCode)
    {
        lastErrorCode = 0;
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(text, copy: true, retryTimes: 30, retryDelay: 100);
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                lastErrorCode = ex.ErrorCode;
                if (VerifyEventuallyMatches(text))
                {
                    // SetDataObject hat sich geirrt (siehe Klassenkommentar) - tatsächlich erfolgreich.
                    return true;
                }

                if (attempt < maxAttempts)
                {
                    System.Threading.Thread.Sleep(700);
                }
            }
        }

        return false;
    }

    private static bool VerifyEventuallyMatches(string expected)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (System.Windows.Forms.Clipboard.GetText() == expected)
                {
                    return true;
                }
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // weiter versuchen, siehe Schleife
            }

            System.Threading.Thread.Sleep(100);
        }

        return false;
    }
}
