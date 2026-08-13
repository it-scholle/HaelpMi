namespace HaelpMi.Core.Models;

/// <summary>
/// One-Shot-Scharfschaltung für den Testmodus-Toggle im Konfigurator (Nutzerwunsch
/// 13.08.2026): "der nächste über den Hotkey ausgelöste Alarm ist ein Test".
///
/// Bewusst kein Bool-Flag, das irgendein Reset-Pfad (Timer-Tick, IPC-Disarm-Call,
/// Prozess-Neustart-Handling) aktiv zurücksetzen müsste - stattdessen nur ein einzelner
/// nullable Zeitstempel, wann scharfgeschaltet wurde. "Noch armiert?" ist dadurch keine
/// gespeicherte Wahrheit, sondern wird bei jedem Zugriff frisch aus der verstrichenen
/// Zeit berechnet. Damit ist <see cref="AppConstants.TestModeTimeout"/> die einzige,
/// unbedingte Sicherheitsgarantie gegen einen liegen gelassenen Toggle (der sonst einen
/// echten Alarm fälschlich als harmlosen Test markieren würde) - sie gilt automatisch,
/// auch wenn ein expliziter <see cref="Disarm"/>-Aufruf (reiner UX-Komfort beim manuellen
/// Wieder-Ausschalten) aus irgendeinem Grund nie ankommt.
/// </summary>
public sealed class TestModeArmState
{
    private readonly object _lock = new();
    private DateTimeOffset? _armedAtUtc;

    /// <summary>Scharfschalten für den nächsten Hotkey-Trigger.</summary>
    public void Arm(DateTimeOffset nowUtc)
    {
        lock (_lock)
        {
            _armedAtUtc = nowUtc;
        }
    }

    /// <summary>Manuelles Wieder-Ausschalten (reiner UX-Komfort, siehe Klassendoku - keine Sicherheitsfunktion).</summary>
    public void Disarm()
    {
        lock (_lock)
        {
            _armedAtUtc = null;
        }
    }

    public bool IsArmed(DateTimeOffset nowUtc)
    {
        lock (_lock)
        {
            return _armedAtUtc is { } armedAt && nowUtc - armedAt < AppConstants.TestModeTimeout;
        }
    }

    public TimeSpan? Remaining(DateTimeOffset nowUtc)
    {
        lock (_lock)
        {
            if (_armedAtUtc is not { } armedAt)
            {
                return null;
            }

            var remaining = AppConstants.TestModeTimeout - (nowUtc - armedAt);
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    /// <summary>
    /// Verbraucht die Scharfschaltung beim nächsten Hotkey-Trigger-Versuch, unabhängig
    /// davon, ob danach überhaupt Empfänger aufgelöst/erreicht werden - "gilt für den
    /// nächsten Hotkey-Trigger", nicht "für den nächsten erfolgreichen Versand". Räumt
    /// auch bei bereits abgelaufenem Fenster auf, damit kein toter Zeitstempel liegen bleibt.
    /// </summary>
    public bool TryConsume(DateTimeOffset nowUtc)
    {
        lock (_lock)
        {
            var wasArmed = _armedAtUtc is { } armedAt && nowUtc - armedAt < AppConstants.TestModeTimeout;
            _armedAtUtc = null;
            return wasArmed;
        }
    }
}
