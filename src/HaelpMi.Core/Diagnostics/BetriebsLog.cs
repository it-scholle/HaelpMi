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
    private long _groesse;

    public BetriebsLog(BetriebsLogOptionen optionen)
    {
        _optionen = optionen;
        var name = Regex.Escape(optionen.Name);
        _aktuelleDatei = new Regex($@"^{name}(?:\.(?<teil>\d+))?\.log$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _archivDatei = new Regex($@"^{name}-(?<tag>\d{{4}}-\d{{2}}-\d{{2}})(?:\.(?<teil>\d+))?\.log(?<gz>\.gz)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
        var log = new BetriebsLog(new BetriebsLogOptionen(SharedLogPaths.ResolveDirectory(AppPaths.RootFolder), name) { MinStufe = stufe });
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
            lock (_sperre)
            {
                var jetzt = _optionen.Zeit.GetLocalNow();
                PruefeTag(DateOnly.FromDateTime(jetzt.DateTime));
                HaengeAn(Formatiere(jetzt, stufe, modul, meldung, objektId, vorgangsId, ausnahme));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Das Betriebs-Log ist das Log; einen Ort, an den ein Fehler beim Schreiben selbst gehen könnte, gibt es nicht.
            // Ein volles oder gesperrtes Laufwerk darf den Alarmweg nie aufhalten.
        }
    }

    private void PruefeTag(DateOnly heute)
    {
        if (!_vorbereitet)
        {
            _vorbereitet = true;
            _tag = heute;
            Directory.CreateDirectory(_optionen.Ordner);
            var teile = AktuelleTeile();
            if (teile.Count > 0)
            {
                var tagDerDatei = DateOnly.FromDateTime(teile.Max(t => File.GetLastWriteTime(t.Pfad)));
                if (tagDerDatei < heute)
                {
                    VersucheWartung(() => Archiviere(teile, tagDerDatei));
                }
                else
                {
                    var letzter = teile[^1];
                    _teil = letzter.Teil;
                    _groesse = new FileInfo(letzter.Pfad).Length;
                }
            }

            VersucheWartung(() => RaeumeAuf(heute));
            return;
        }

        if (heute != _tag)
        {
            var gestern = _tag;
            VersucheWartung(() => Archiviere(AktuelleTeile(), gestern));
            _tag = heute;
            _teil = 0;
            _groesse = 0;
            VersucheWartung(() => RaeumeAuf(heute));
        }
    }

    /// <summary>Umbenennen, Komprimieren und Löschen dürfen den Eintrag selbst nicht verhindern; ein Fehlschlag kommt als WARNUNG ins Log.</summary>
    private void VersucheWartung(Action wartung)
    {
        try
        {
            wartung();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SchreibeWarnungIntern("Umbenennen oder Aufräumen der Log-Dateien fehlgeschlagen, wird beim nächsten Tageswechsel oder Start wiederholt.", ex);
        }
    }

    private void HaengeAn(string zeile)
    {
        var bytes = Utf8OhneBom.GetBytes(zeile + "\n");
        if (_groesse > 0 && _groesse + bytes.Length > _optionen.MaxDateiBytes)
        {
            var voll = Pfad(_teil);
            _teil++;
            _groesse = 0;
            SchreibeWarnungIntern($"{Path.GetFileName(voll)} hat die Grenze von {_optionen.MaxDateiBytes / (1024 * 1024)} MB erreicht, weiter in {Path.GetFileName(Pfad(_teil))}.");
            VersucheWartung(() => RaeumeAuf(_tag));
        }

        SchreibeIntern(bytes);
    }

    private void SchreibeWarnungIntern(string meldung, Exception? ausnahme = null) =>
        SchreibeIntern(Utf8OhneBom.GetBytes(Formatiere(_optionen.Zeit.GetLocalNow(), LogStufe.Warnung, nameof(BetriebsLog), meldung, null, null, ausnahme) + "\n"));

    private void SchreibeIntern(byte[] bytes)
    {
        // FileShare.ReadWrite: ein Agent je angemeldeter Sitzung (Terminalserver) schreibt in dieselbe Datei.
        using (var datei = new FileStream(Pfad(_teil), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            datei.Write(bytes);
        }

        _groesse += bytes.Length;
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
            if (_aktuelleDatei.Match(name) is { Success: true } aktuell)
            {
                ergebnis.Add(new Datei(pfad, TeilAus(aktuell), null, false));
            }
            else if (_archivDatei.Match(name) is { Success: true } archiv
                     && DateOnly.TryParseExact(archiv.Groups["tag"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag))
            {
                ergebnis.Add(new Datei(pfad, TeilAus(archiv), tag, archiv.Groups["gz"].Success));
            }
        }

        return ergebnis;
    }

    private static int TeilAus(Match treffer) =>
        treffer.Groups["teil"].Success ? int.Parse(treffer.Groups["teil"].Value, CultureInfo.InvariantCulture) : 0;

    private void Archiviere(List<Datei> teile, DateOnly tag)
    {
        foreach (var teil in teile)
        {
            var ziel = ArchivPfad(tag, teil.Teil);
            try
            {
                if (File.Exists(ziel))
                {
                    // Nur nach einem Neustart am selben Tag über Mitternacht hinweg möglich: anhängen statt eine zweite Datei.
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

        var gesamt = dateien.Sum(d => new FileInfo(d.Pfad).Length);
        var loeschbar = dateien
            .Where(d => !(d.Tag is null && d.Teil == _teil))
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

            var laenge = new FileInfo(datei.Pfad).Length;
            File.Delete(datei.Pfad);
            gesamt -= laenge;
            SchreibeWarnungIntern($"Logs von „{_optionen.Name}“ über {_optionen.MaxGesamtBytes / (1024 * 1024)} MB, älteste Datei gelöscht: {Path.GetFileName(datei.Pfad)}.");
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
