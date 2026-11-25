using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Diagnostics;

/// <summary>
/// Betriebs-Log eines Prozesses nach AGENTS.md Teil A: eine JSON-Zeile je Eintrag, aktuelle Datei
/// <c>&lt;name&gt;.log</c>, beim Tageswechsel (auch beim Start) umbenannt in <c>&lt;name&gt;-JJJJ-MM-TT.log</c>
/// mit dem Datum der Einträge. Notbremse: volle Datei → WARNUNG und weiter in <c>&lt;name&gt;.1.log</c> usw.;
/// mehr als die Gesamtgrenze → älteste Dateien löschen und warnen. Archive werden nach der Aufbewahrungsfrist
/// gelöscht und vorher komprimiert. Nie Alarmtexte, Secrets oder andere Nutzerinhalte übergeben.
/// </summary>
public sealed class BetriebsLog
{
    public const long StandardMaxDateiBytes = 20L * 1024 * 1024;
    public const long StandardMaxGesamtBytes = 200L * 1024 * 1024;

    private static readonly JsonWriterOptions JsonOptionen = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly UTF8Encoding Utf8OhneBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _sperre = new();
    private readonly BetriebsLogOptionen _optionen;
    private readonly Regex _aktuelleDatei;
    private readonly Regex _archivDatei;
    private bool _vorbereitet;
    private DateOnly _tag;
    private int _teil;

    public BetriebsLog(BetriebsLogOptionen optionen)
    {
        _optionen = optionen;
        var name = Regex.Escape(optionen.Name);
        _aktuelleDatei = new Regex($@"^{name}(?:\.(?<teil>[0-9]+))?\.log$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _archivDatei = new Regex($@"^{name}-(?<tag>[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}})(?:\.(?<teil>[0-9]+))?\.log(?<gz>\.gz)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Log des laufenden Prozesses; vor <see cref="StarteFuerProzess"/> verwerfen die statischen Methoden alles.</summary>
    public static BetriebsLog? Prozess { get; private set; }

    public LogStufe MinStufe => _optionen.MinStufe;

    /// <summary>
    /// Legt das Log für diesen Prozess an (Ablage wie crash.log) und liest die Stufe aus
    /// <see cref="AppPaths.BetriebsLogEinstellungenPath"/>; ohne Datei gilt INFO.
    /// </summary>
    /// <param name="name">Fester Dateiname ohne Endung, z. B. „agent“.</param>
    public static BetriebsLog StarteFuerProzess(string name)
    {
        var stufe = LeseStufe(AppPaths.BetriebsLogEinstellungenPath, out var hinweis);
        string ordner;
        try
        {
            ordner = SharedLogPaths.ResolveDirectory(AppPaths.RootFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ohne beschreibbaren Ordner schreibt Schreibe nichts; der Start des Prozesses darf daran nicht scheitern.
            ordner = AppPaths.RootFolder;
        }

        var log = new BetriebsLog(new BetriebsLogOptionen(ordner, name) { MinStufe = stufe });
        Prozess = log;
        if (hinweis is not null)
        {
            log.Schreibe(LogStufe.Warnung, nameof(BetriebsLog), hinweis);
        }

        return log;
    }

    public static void Fehler(string modul, string meldung, Exception? ausnahme = null, string? objektId = null, string? vorgangsId = null) =>
        Prozess?.Schreibe(LogStufe.Fehler, modul, meldung, objektId, vorgangsId, ausnahme);

    public static void Warnung(string modul, string meldung, Exception? ausnahme = null, string? objektId = null, string? vorgangsId = null) =>
        Prozess?.Schreibe(LogStufe.Warnung, modul, meldung, objektId, vorgangsId, ausnahme);

    public static void Info(string modul, string meldung, string? objektId = null, string? vorgangsId = null) =>
        Prozess?.Schreibe(LogStufe.Info, modul, meldung, objektId, vorgangsId);

    public static void Debug(string modul, string meldung, string? objektId = null, string? vorgangsId = null) =>
        Prozess?.Schreibe(LogStufe.Debug, modul, meldung, objektId, vorgangsId);

    /// <summary>
    /// Liest <c>{"Stufe": "Debug"}</c> (FEHLER, WARNUNG, INFO, DEBUG bzw. die Enum-Namen, Groß-/Kleinschreibung egal).
    /// Fehlt die Datei, gilt INFO ohne Hinweis; ist sie unlesbar oder der Wert unbekannt, INFO mit Hinweis.
    /// </summary>
    public static LogStufe LeseStufe(string pfad, out string? hinweis)
    {
        hinweis = null;
        if (!File.Exists(pfad))
        {
            return LogStufe.Info;
        }

        try
        {
            using var dokument = JsonDocument.Parse(File.ReadAllText(pfad));
            if (dokument.RootElement.ValueKind == JsonValueKind.Object
                && dokument.RootElement.TryGetProperty("Stufe", out var wert)
                && wert.ValueKind == JsonValueKind.String
                && ParseStufe(wert.GetString()) is { } stufe)
            {
                return stufe;
            }

            hinweis = $"{pfad}: \"Stufe\" fehlt oder ist unbekannt (erlaubt: FEHLER, WARNUNG, INFO, DEBUG), es gilt INFO.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            hinweis = $"{pfad} ist nicht lesbar ({ex.GetType().Name}: {ex.Message}), es gilt INFO.";
        }

        return LogStufe.Info;
    }

    private static LogStufe? ParseStufe(string? text) => text?.Trim().ToUpperInvariant() switch
    {
        "FEHLER" => LogStufe.Fehler,
        "WARNUNG" => LogStufe.Warnung,
        "INFO" => LogStufe.Info,
        "DEBUG" => LogStufe.Debug,
        _ => null,
    };

    private static string Bezeichnung(LogStufe stufe) => stufe switch
    {
        LogStufe.Fehler => "FEHLER",
        LogStufe.Warnung => "WARNUNG",
        LogStufe.Info => "INFO",
        _ => "DEBUG",
    };

    public void Schreibe(LogStufe stufe, string modul, string meldung, string? objektId = null, string? vorgangsId = null, Exception? ausnahme = null)
    {
        if (stufe < _optionen.MinStufe)
        {
            return;
        }

        try
        {
            DateOnly? aufraeumenFuer;
            lock (_sperre)
            {
                var jetzt = _optionen.Zeit.GetLocalNow();
                aufraeumenFuer = PruefeTag(DateOnly.FromDateTime(jetzt.DateTime));
                if (HaengeAn(Formatiere(jetzt, stufe, modul, meldung, objektId, vorgangsId, ausnahme)))
                {
                    aufraeumenFuer = _tag;
                }
            }

            if (aufraeumenFuer is { } tag)
            {
                StarteAufraeumen(tag);
            }
        }
        catch (Exception)
        {
            // Das Betriebs-Log ist das Log; einen Ort, an den ein Fehler beim Schreiben selbst gehen könnte, gibt es nicht.
            // Ein volles oder gesperrtes Laufwerk oder eine fremde Datei im Ordner darf den Alarmweg nie aufhalten.
        }
    }

    /// <summary>Hintergrund-Aufräumen (Komprimieren, Aufbewahrung, Gesamtgrenze); nur für Tests zum Abwarten.</summary>
    internal Task Aufraeumen { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Gleicht beim ersten Eintrag und beim Tageswechsel mit den Dateien ab. Mehrere Agents (eine Sitzung je Nutzer)
    /// teilen sich die Dateien: Wer zuerst kommt, benennt um; das Datum kommt aus der letzten Änderung der Datei,
    /// also aus ihren Einträgen. Gibt den Tag zurück, für den aufgeräumt werden soll.
    /// </summary>
    private DateOnly? PruefeTag(DateOnly heute)
    {
        if (_vorbereitet && heute == _tag)
        {
            return null;
        }

        Directory.CreateDirectory(_optionen.Ordner);
        var teile = AktuelleTeile();
        if (teile.Count > 0)
        {
            var tagDerDatei = DateOnly.FromDateTime(teile.Max(t => File.GetLastWriteTime(t.Pfad)));
            if (tagDerDatei < heute)
            {
                VersucheWartung(() => Archiviere(teile, tagDerDatei));
                teile = AktuelleTeile();
            }
        }

        _teil = teile.Count > 0 ? teile[^1].Teil : 0;
        _tag = heute;
        _vorbereitet = true;
        return heute;
    }

    /// <summary>Gibt true zurück, wenn eine neue Teil-Datei begonnen wurde.</summary>
    private bool HaengeAn(string zeile)
    {
        var bytes = Utf8OhneBom.GetBytes(zeile + "\n");
        var neuerTeil = false;
        if (Groesse(_teil) is var groesse && groesse > 0 && groesse + bytes.Length > _optionen.MaxDateiBytes)
        {
            // Ein anderer Agent kann schon weitergeschaltet haben; die Größe kommt deshalb aus der Datei, nicht aus einem Zähler.
            var hoechster = AktuelleTeile().Select(t => t.Teil).DefaultIfEmpty(0).Max();
            if (hoechster > _teil && Groesse(hoechster) + bytes.Length <= _optionen.MaxDateiBytes)
            {
                _teil = hoechster;
            }
            else
            {
                var voll = Pfad(Math.Max(hoechster, _teil));
                _teil = Math.Max(hoechster, _teil) + 1;
                neuerTeil = true;
                SchreibeIntern(Utf8OhneBom.GetBytes(Formatiere(_optionen.Zeit.GetLocalNow(), LogStufe.Warnung, nameof(BetriebsLog),
                    $"{Path.GetFileName(voll)} hat die Grenze von {MegaByte(_optionen.MaxDateiBytes)} erreicht, weiter in {Path.GetFileName(Pfad(_teil))}.",
                    null, null, null) + "\n"));
            }
        }

        SchreibeIntern(bytes);
        return neuerTeil;
    }

    private long Groesse(int teil)
    {
        var info = new FileInfo(Pfad(teil));
        return info.Exists ? info.Length : 0;
    }

    private static string MegaByte(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.##", CultureInfo.InvariantCulture) + " MB";

    private void SchreibeIntern(byte[] bytes)
    {
        // FileShare.ReadWrite: ein Agent je angemeldeter Sitzung (Terminalserver) schreibt in dieselbe Datei.
        using var datei = new FileStream(Pfad(_teil), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        datei.Write(bytes);
    }

    private void VersucheWartung(Action wartung)
    {
        try
        {
            wartung();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SchreibeIntern(Utf8OhneBom.GetBytes(Formatiere(_optionen.Zeit.GetLocalNow(), LogStufe.Warnung, nameof(BetriebsLog),
                "Umbenennen der Log-Dateien fehlgeschlagen, wird beim nächsten Tageswechsel oder Start wiederholt.", null, null, ex) + "\n"));
        }
    }

    /// <summary>Komprimieren und Löschen kann bei großen Archiven auf Z: dauern; der Eintrag und der Alarmweg warten nicht darauf.</summary>
    private void StarteAufraeumen(DateOnly heute)
    {
        lock (_sperre)
        {
            if (!Aufraeumen.IsCompleted)
            {
                return;
            }

            Aufraeumen = Task.Run(() =>
            {
                try
                {
                    RaeumeAuf(heute);
                }
                catch (Exception ex)
                {
                    Schreibe(LogStufe.Warnung, nameof(BetriebsLog), "Aufräumen der Log-Dateien fehlgeschlagen, wird beim nächsten Tageswechsel oder Start wiederholt.", ausnahme: ex);
                }
            });
        }
    }

    private string Pfad(int teil) => Path.Combine(_optionen.Ordner, teil == 0 ? $"{_optionen.Name}.log" : $"{_optionen.Name}.{teil}.log");

    private string ArchivPfad(DateOnly tag, int teil) => Path.Combine(_optionen.Ordner,
        $"{_optionen.Name}-{tag.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{(teil == 0 ? string.Empty : "." + teil)}.log");

    private readonly record struct Datei(string Pfad, int Teil, DateOnly? Tag, bool Komprimiert);

    private List<Datei> AktuelleTeile() => Dateien().Where(d => d.Tag is null).OrderBy(d => d.Teil).ToList();

    private List<Datei> Dateien()
    {
        var ergebnis = new List<Datei>();
        foreach (var pfad in Directory.EnumerateFiles(_optionen.Ordner))
        {
            var name = Path.GetFileName(pfad);
            if (_aktuelleDatei.Match(name) is { Success: true } aktuell && TeilAus(aktuell) is { } teil)
            {
                ergebnis.Add(new Datei(pfad, teil, null, false));
            }
            else if (_archivDatei.Match(name) is { Success: true } archiv
                     && TeilAus(archiv) is { } archivTeil
                     && DateOnly.TryParseExact(archiv.Groups["tag"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag))
            {
                ergebnis.Add(new Datei(pfad, archivTeil, tag, archiv.Groups["gz"].Success));
            }
        }

        return ergebnis;
    }

    /// <summary>null bei einer Teilnummer außerhalb von int, z. B. eine fremde Datei „agent.99999999999.log“: wird ignoriert.</summary>
    private static int? TeilAus(Match treffer)
    {
        if (!treffer.Groups["teil"].Success)
        {
            return 0;
        }

        return int.TryParse(treffer.Groups["teil"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var teil) ? teil : null;
    }

    private void Archiviere(List<Datei> teile, DateOnly tag)
    {
        foreach (var teil in teile)
        {
            var ziel = ArchivPfad(tag, teil.Teil);
            try
            {
                if (File.Exists(ziel))
                {
                    // Neustart nach Mitternacht, bevor die Datei des Vortags umbenannt war, oder zwei Agents gleichzeitig:
                    // anhängen statt eine zweite Datei für denselben Tag.
                    File.AppendAllText(ziel, File.ReadAllText(teil.Pfad, Utf8OhneBom), Utf8OhneBom);
                    File.Delete(teil.Pfad);
                }
                else
                {
                    File.Move(teil.Pfad, ziel);
                }
            }
            catch (FileNotFoundException)
            {
                // Ein zweiter Agent (andere Sitzung) hat die Datei schon umbenannt.
            }
        }
    }

    private void RaeumeAuf(DateOnly heute)
    {
        var dateien = Dateien();

        foreach (var alt in dateien.Where(d => d.Tag is { } tag && tag < heute.AddDays(-_optionen.AufbewahrungTage)).ToList())
        {
            File.Delete(alt.Pfad);
            dateien.Remove(alt);
        }

        for (var i = 0; i < dateien.Count; i++)
        {
            if (dateien[i] is { Tag: { } tag, Komprimiert: false } archiv && tag <= heute.AddDays(-_optionen.KomprimierenAbTagen))
            {
                dateien[i] = Komprimiere(archiv);
            }
        }

        int aktiverTeil;
        lock (_sperre)
        {
            aktiverTeil = _teil;
        }

        var gesamt = dateien.Sum(d => new FileInfo(d.Pfad).Length);
        var loeschbar = dateien
            .Where(d => !(d.Tag is null && d.Teil >= aktiverTeil))
            .OrderBy(d => d.Tag is null ? 1 : 0)
            .ThenBy(d => d.Tag)
            .ThenBy(d => d.Teil)
            .ToList();
        foreach (var datei in loeschbar)
        {
            if (gesamt <= _optionen.MaxGesamtBytes)
            {
                break;
            }

            gesamt -= new FileInfo(datei.Pfad).Length;
            File.Delete(datei.Pfad);
            Schreibe(LogStufe.Warnung, nameof(BetriebsLog),
                $"Logs von „{_optionen.Name}“ über {MegaByte(_optionen.MaxGesamtBytes)}, älteste Datei gelöscht: {Path.GetFileName(datei.Pfad)}.");
        }
    }

    private static Datei Komprimiere(Datei archiv)
    {
        var ziel = archiv.Pfad + ".gz";
        using (var quelle = File.OpenRead(archiv.Pfad))
        using (var gz = new GZipStream(File.Create(ziel), CompressionLevel.Optimal))
        {
            quelle.CopyTo(gz);
        }

        File.Delete(archiv.Pfad);
        return archiv with { Pfad = ziel, Komprimiert = true };
    }

    private string Formatiere(DateTimeOffset zeit, LogStufe stufe, string modul, string meldung, string? objektId, string? vorgangsId, Exception? ausnahme)
    {
        using var puffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(puffer, JsonOptionen))
        {
            json.WriteStartObject();
            json.WriteString("zeit", zeit.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
            json.WriteString("stufe", Bezeichnung(stufe));
            json.WriteString("prozess", _optionen.Name);
            json.WriteNumber("pid", Environment.ProcessId);
            json.WriteString("modul", modul);
            json.WriteString("meldung", meldung);
            if (objektId is not null)
            {
                json.WriteString("objekt", objektId);
            }

            if (vorgangsId is not null)
            {
                json.WriteString("vorgang", vorgangsId);
            }

            if (ausnahme is not null)
            {
                json.WriteString("ausnahme", ausnahme.ToString());
            }

            json.WriteEndObject();
        }

        return Utf8OhneBom.GetString(puffer.ToArray());
    }
}
