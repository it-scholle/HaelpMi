using System.Text.Json;

namespace HaelpMi.Core.Diagnostics;

/// <summary>
/// Meldet verworfene Nachrichten fremder Kundengruppen gedrosselt als WARNUNG ins Betriebs-Log, nie ins
/// Audit-Log: höchstens eine Meldung je Kombination aus Nachrichtenart und fremder Kundengruppe pro Intervall,
/// die nächste Meldung nennt die Zahl der seither verworfenen Nachrichten. Bleibt rein lokal; der fremden
/// Installation wird weiterhin nichts geantwortet.
/// </summary>
public sealed class FremdeKundengruppeMelder
{
    public const string Modul = "Kundengruppe";
    public const string EinstellungDrosselMinuten = "FremdeKundengruppeDrosselMinuten";
    public static readonly TimeSpan StandardIntervall = TimeSpan.FromHours(1);

    /// <summary>Obergrenze der gemerkten Kombinationen, damit Nachrichten mit ständig neuen IDs den Speicher nicht füllen.</summary>
    public const int StandardMaxKombinationen = 256;

    private readonly object _sperre = new();
    private readonly Dictionary<(string Art, Guid Gruppe), Zaehler> _kombinationen = [];
    private readonly TimeSpan _intervall;
    private readonly int _maxKombinationen;
    private readonly TimeProvider _zeit;
    private readonly Action<string, string?> _schreibeWarnung;
    private long? _letzteUeberlaufMeldung;
    private long _ueberlaufSeitMeldung;

    /// <param name="schreibeWarnung">Meldung und Objekt-ID (Absender-Geräte-ID); Standard: <see cref="BetriebsLog.Warnung"/>.</param>
    public FremdeKundengruppeMelder(
        TimeSpan? intervall = null,
        Action<string, string?>? schreibeWarnung = null,
        TimeProvider? zeit = null,
        int maxKombinationen = StandardMaxKombinationen)
    {
        _intervall = intervall ?? StandardIntervall;
        _schreibeWarnung = schreibeWarnung ?? ((meldung, objektId) => BetriebsLog.Warnung(Modul, meldung, objektId: objektId));
        _zeit = zeit ?? TimeProvider.System;
        _maxKombinationen = maxKombinationen;
    }

    /// <summary>Instanz des laufenden Prozesses, die <see cref="Networking.Protocol.CustomerGroupFilter"/> nutzt.</summary>
    public static FremdeKundengruppeMelder Prozess { get; set; } = new();

    /// <param name="nachrichtenart">Feste Bezeichnung der Nachrichtenart, z. B. „Alarm“; nie Nachrichteninhalt.</param>
    /// <param name="absenderGeraetId">Geräte-ID des Absenders, falls die Nachricht eine trägt.</param>
    public void Verworfen(string nachrichtenart, Guid fremdeKundengruppe, Guid? absenderGeraetId)
    {
        string? meldung;
        lock (_sperre)
        {
            meldung = Zaehle(nachrichtenart, fremdeKundengruppe, _zeit.GetTimestamp());
        }

        if (meldung is not null)
        {
            _schreibeWarnung(meldung, absenderGeraetId?.ToString());
        }
    }

    // Monotone Zeitstempel: Eine zurückgestellte Systemuhr darf die Meldungen nicht verstummen lassen.
    private string? Zaehle(string art, Guid gruppe, long jetzt)
    {
        var schluessel = (art, gruppe);
        if (!_kombinationen.TryGetValue(schluessel, out var zaehler))
        {
            if (_kombinationen.Count >= _maxKombinationen)
            {
                EntferneAbgelaufene(jetzt);
            }

            if (_kombinationen.Count >= _maxKombinationen)
            {
                return ZaehleUeberlauf(jetzt);
            }

            _kombinationen[schluessel] = new Zaehler { LetzteMeldung = jetzt };
            return Text(art, gruppe, 1);
        }

        zaehler.SeitMeldung++;
        if (_zeit.GetElapsedTime(zaehler.LetzteMeldung, jetzt) < _intervall)
        {
            return null;
        }

        var anzahl = zaehler.SeitMeldung;
        zaehler.SeitMeldung = 0;
        zaehler.LetzteMeldung = jetzt;
        return Text(art, gruppe, anzahl);
    }

    private void EntferneAbgelaufene(long jetzt)
    {
        // Abgelaufene Kombinationen mit ungemeldeten Nachrichten gehen dabei verloren; sie tauchen wieder auf, sobald die Gruppe erneut sendet.
        foreach (var schluessel in _kombinationen.Where(k => _zeit.GetElapsedTime(k.Value.LetzteMeldung, jetzt) >= _intervall).Select(k => k.Key).ToList())
        {
            _kombinationen.Remove(schluessel);
        }
    }

    private string? ZaehleUeberlauf(long jetzt)
    {
        _ueberlaufSeitMeldung++;
        if (_letzteUeberlaufMeldung is { } letzte && _zeit.GetElapsedTime(letzte, jetzt) < _intervall)
        {
            return null;
        }

        var anzahl = _ueberlaufSeitMeldung;
        _ueberlaufSeitMeldung = 0;
        _letzteUeberlaufMeldung = jetzt;
        return $"Nachrichten weiterer fremder Kundengruppen verworfen (mehr als {_maxKombinationen} Kombinationen aus Art und Gruppe), "
            + $"Anzahl seit der letzten Meldung: {anzahl}. Läuft im selben Netz eine andere HälpMi-Installation oder ein falsch gebauter Installer?";
    }

    private string Text(string art, Guid gruppe, long anzahl) =>
        $"Nachricht einer fremden Kundengruppe verworfen: Art {art}, Kundengruppe {Maskiere(gruppe)}, Anzahl seit der letzten Meldung: {anzahl}. "
        + $"Weitere Meldungen dazu höchstens alle {_intervall.TotalMinutes:0} Minuten. Läuft im selben Netz eine andere HälpMi-Installation "
        + "oder wurde dieses Gerät mit einem Installer einer anderen Kundengruppe installiert?";

    /// <summary>Nur die ersten 8 Zeichen: genug, um zwei Gruppen auseinanderzuhalten, ohne die volle ID ins Log zu schreiben.</summary>
    internal static string Maskiere(Guid gruppe) => gruppe.ToString("N")[..8] + "…";

    /// <summary>
    /// Liest <c>FremdeKundengruppeDrosselMinuten</c> (ganze Zahl ≥ 1) aus der Datei des Betriebs-Logs.
    /// Fehlt Datei oder Wert, gilt <see cref="StandardIntervall"/> ohne Hinweis; ist der Wert ungültig, mit Hinweis.
    /// </summary>
    public static TimeSpan LeseIntervall(string pfad, out string? hinweis)
    {
        hinweis = null;
        if (!File.Exists(pfad))
        {
            return StandardIntervall;
        }

        try
        {
            using var dokument = JsonDocument.Parse(File.ReadAllText(pfad));
            if (dokument.RootElement.ValueKind != JsonValueKind.Object
                || !dokument.RootElement.TryGetProperty(EinstellungDrosselMinuten, out var wert))
            {
                return StandardIntervall;
            }

            if (wert.ValueKind == JsonValueKind.Number && wert.TryGetInt32(out var minuten) && minuten >= 1)
            {
                return TimeSpan.FromMinutes(minuten);
            }

            hinweis = $"{pfad}: \"{EinstellungDrosselMinuten}\" muss eine ganze Zahl ab 1 sein, es gilt {StandardIntervall.TotalMinutes:0} Minuten.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            hinweis = $"{pfad} ist nicht lesbar ({ex.GetType().Name}: {ex.Message}), Drosselung fremder Kundengruppen: {StandardIntervall.TotalMinutes:0} Minuten.";
        }

        return StandardIntervall;
    }

    private sealed class Zaehler
    {
        public long LetzteMeldung { get; set; }

        public long SeitMeldung { get; set; }
    }
}
