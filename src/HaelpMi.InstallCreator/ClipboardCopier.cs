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
/// retryTimes/retryDelay-Parameter für CLIPBRD_E_CANT_OPEN, meldet aber ebenfalls gelegentlich
/// fälschlich CLIPBRD_E_CANT_OPEN aus seinem eigenen internen Render-/Flush-Schritt, obwohl der
/// eigentliche Schreibvorgang längst angekommen ist - deshalb das Rücklese-Verify vor jedem
/// "wirklich fehlgeschlagen".
///
/// Nachtrag 08.09.2026 (Fehlerbericht "immer noch ~20s Programm-Hänger trotz Fix"): der obige
/// retryTimes/retryDelay-Parameter deckt nur ein kurzes "busy, bitte nochmal versuchen" ab.
/// Hält ein fremder Prozess (z. B. VM-Zwischenablage-Synchronisation) die Zwischenablage
/// tatsächlich länger blockiert, kann der einzelne SetDataObject-Aufruf selbst - nicht nur die
/// Pause zwischen zwei Versuchen - viele Sekunden lang im Win32/OLE-Aufruf hängen bleiben; kein
/// retryTimes-Wert kann die Dauer eines einzelnen bereits blockierenden Aufrufs begrenzen. Der
/// vorherige zweite Voll-Anlauf (weiterer SetDataObject-Aufruf + erneutes Rücklese-Verify)
/// verdoppelte diese unbegrenzte Wartezeit nur, ohne sie zu begrenzen. Deshalb läuft der
/// eigentliche Kopierversuch jetzt auf einem eigenen Hintergrund-Thread; das UI wartet über
/// <see cref="Thread.Join(int)"/> höchstens <see cref="TotalTimeoutMs"/> darauf - danach gilt
/// der Versuch als fehlgeschlagen und das UI ist wieder bedienbar, unabhängig davon, wie lange
/// der Fremdprozess die Zwischenablage noch blockiert. Der Hintergrund-Thread selbst läuft als
/// Daemon (<see cref="Thread.IsBackground"/>) unbeobachtet weiter oder stirbt mit dem Prozess -
/// kein Leck, da Kopierversuche nicht gehäuft auftreten.
/// </summary>
internal static class ClipboardCopier
{
    private const int TotalTimeoutMs = 2500;
    private const int SetDataRetryTimes = 10;
    private const int SetDataRetryDelayMs = 100;
    private const int VerifyAttempts = 5;
    private const int VerifyDelayMs = 100;

    public static bool TryCopy(string text, out int lastErrorCode)
    {
        var outcome = new CopyOutcome();
        var worker = new System.Threading.Thread(() => RunCopy(text, outcome)) { IsBackground = true };
        worker.SetApartmentState(System.Threading.ApartmentState.STA);
        worker.Start();

        if (!worker.Join(TotalTimeoutMs))
        {
            // Fremdprozess blockiert die Zwischenablage länger als hinnehmbar - UI nicht länger
            // aufhalten, siehe Klassenkommentar. Kein spezifischer HRESULT bekannt, da der
            // Aufruf selbst nie zurückgekehrt ist.
            lastErrorCode = 0;
            return false;
        }

        lastErrorCode = outcome.ErrorCode;
        return outcome.Success;
    }

    private static void RunCopy(string text, CopyOutcome outcome)
    {
        try
        {
            System.Windows.Forms.Clipboard.SetDataObject(text, copy: true, retryTimes: SetDataRetryTimes, retryDelay: SetDataRetryDelayMs);
            outcome.Success = true;
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            outcome.ErrorCode = ex.ErrorCode;
            // SetDataObject hat sich möglicherweise geirrt (siehe Klassenkommentar) -
            // tatsächlich erfolgreich trotz gemeldeter Exception.
            outcome.Success = VerifyEventuallyMatches(text);
        }
    }

    private static bool VerifyEventuallyMatches(string expected)
    {
        for (var attempt = 0; attempt < VerifyAttempts; attempt++)
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

            System.Threading.Thread.Sleep(VerifyDelayMs);
        }

        return false;
    }

    private sealed class CopyOutcome
    {
        public bool Success;
        public int ErrorCode;
    }
}
