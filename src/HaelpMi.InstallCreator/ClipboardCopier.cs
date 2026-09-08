namespace HaelpMi.InstallCreator;

/// <summary>
/// **Regel (Nutzerentscheidung 01.09.2026): jede Zwischenablage-Kopie in diesem Projekt läuft
/// über <see cref="TryCopyAsync"/>, nie über einen direkten Clipboard-Aufruf.** Grund,
/// ausführlich hier statt an jeder Aufrufstelle wiederholt (siehe ursprüngliche
/// Fehlerbericht-Kette vom 06./11.08.2026 zum Passwort-Kopieren-Knopf für die volle
/// Vorgeschichte):
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
/// Nachtrag 08.09.2026, erster Anlauf ("immer noch ~20s Programm-Hänger trotz Fix"): ein
/// fremder Prozess (hier: UTM-Zwischenablage-Synchronisation zwischen macOS-Host und
/// Windows-Gast) kann die Zwischenablage tatsächlich blockieren - dann hängt der einzelne
/// SetDataObject-Aufruf selbst, nicht nur die Pause zwischen zwei Versuchen, im Win32/OLE-Call
/// fest. Erster Versuch, das zu begrenzen: der Kopierversuch lief auf einem Hintergrund-Thread,
/// das UI wartete aber weiterhin synchron (<see cref="Thread.Join(int)"/>) mit hartem
/// Zeitlimit darauf.
///
/// Nachtrag 08.09.2026, zweiter Anlauf (Fehlerbericht "meldet jetzt HRESULT 0x00000000, kopiert
/// aber gar nichts mehr"): die UTM-Blockade dauert regelmäßig länger als jedes UI-verträgliche
/// Zeitlimit - ein hartes Limit bedeutet dann nur noch "gibt garantiert zu früh auf", nie
/// "funktioniert". Der eigentliche Fehler war, überhaupt synchron auf den Hintergrund-Thread zu
/// warten: <see cref="TryCopyAsync"/> gibt der aufrufenden UI die Kontrolle sofort zurück
/// (kein <c>Join</c> mehr), das Ergebnis kommt per <see cref="Task"/>, wenn es vorliegt - das UI
/// friert dadurch gar nicht mehr ein, unabhängig davon, wie lange der Fremdprozess tatsächlich
/// braucht. <see cref="TotalTimeoutMs"/> ist dadurch keine UI-Wartegrenze mehr, sondern nur
/// noch eine Absicherung gegen einen für immer blockierten Hintergrund-Thread (der sonst bis
/// zum Prozessende offen bliebe) - kann daher grosszügig bemessen sein.
/// </summary>
internal static class ClipboardCopier
{
    private const int TotalTimeoutMs = 15000;
    private const int SetDataRetryTimes = 10;
    private const int SetDataRetryDelayMs = 100;
    private const int VerifyAttempts = 5;
    private const int VerifyDelayMs = 100;

    public static Task<(bool Success, int ErrorCode)> TryCopyAsync(string text)
    {
        var completion = new TaskCompletionSource<(bool Success, int ErrorCode)>();

        var worker = new System.Threading.Thread(() =>
        {
            var outcome = new CopyOutcome();
            RunCopy(text, outcome);
            completion.TrySetResult((outcome.Success, outcome.ErrorCode));
        })
        { IsBackground = true };
        worker.SetApartmentState(System.Threading.ApartmentState.STA);
        worker.Start();

        // Falls der Thread nie zurückkehrt (siehe Klassenkommentar): den Task trotzdem
        // irgendwann auflösen, statt ihn bis zum Prozessende offen zu lassen.
        _ = Task.Delay(TotalTimeoutMs).ContinueWith(
            _ => completion.TrySetResult((false, 0)),
            TaskScheduler.Default);

        return completion.Task;
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
