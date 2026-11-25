using System.IO.Compression;
using System.Text.Json;
using HaelpMi.Core.Diagnostics;
using Xunit;

namespace HaelpMi.Core.Tests;

public sealed class BetriebsLogTests : IDisposable
{
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "HaelpMiBetriebsLog_" + Guid.NewGuid().ToString("N"));
    private readonly TestZeit _zeit = new(DateTimeOffset.Now);

    public BetriebsLogTests() => Directory.CreateDirectory(_ordner);

    private readonly List<BetriebsLog> _logs = [];

    public void Dispose()
    {
        foreach (var log in _logs)
        {
            Assert.True(log.Aufraeumen.Wait(TimeSpan.FromSeconds(10)), "Aufräumen nicht nach 10 s fertig.");
        }

        try
        {
            Directory.Delete(_ordner, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // nur Aufräumen des Temp-Ordners
        }
    }

    private BetriebsLog NeuesLog(Func<BetriebsLogOptionen, BetriebsLogOptionen>? anpassen = null)
    {
        var optionen = new BetriebsLogOptionen(_ordner, "agent") { Zeit = _zeit };
        var log = new BetriebsLog(anpassen?.Invoke(optionen) ?? optionen);
        _logs.Add(log);
        return log;
    }

    private string Datei(string name) => Path.Combine(_ordner, name);

    private string HeuteText => _zeit.GetLocalNow().ToString("yyyy-MM-dd");

    private static List<JsonElement> Zeilen(string pfad) =>
        File.ReadAllLines(pfad).Select(z => JsonDocument.Parse(z).RootElement.Clone()).ToList();

    [Fact]
    public void Eintrag_IstEineJsonZeile_MitZeitzoneStufeModulMeldungObjektUndVorgang()
    {
        NeuesLog().Schreibe(LogStufe.Info, "Alarm", "Welle gesendet", objektId: "geraet-1", vorgangsId: "sitzung-7");

        var zeile = Assert.Single(Zeilen(Datei("agent.log")));
        var zeit = zeile.GetProperty("zeit").GetString()!;
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}[+-]\d{2}:\d{2}$", zeit);
        Assert.Equal("INFO", zeile.GetProperty("stufe").GetString());
        Assert.Equal("agent", zeile.GetProperty("prozess").GetString());
        Assert.Equal(Environment.ProcessId, zeile.GetProperty("pid").GetInt32());
        Assert.Equal("Alarm", zeile.GetProperty("modul").GetString());
        Assert.Equal("Welle gesendet", zeile.GetProperty("meldung").GetString());
        Assert.Equal("geraet-1", zeile.GetProperty("objekt").GetString());
        Assert.Equal("sitzung-7", zeile.GetProperty("vorgang").GetString());
    }

    [Fact]
    public void Ausnahme_WirdMitTypUndStacktraceGeschrieben()
    {
        Exception gefangen;
        try
        {
            throw new InvalidOperationException("kaputt");
        }
        catch (InvalidOperationException ex)
        {
            gefangen = ex;
        }

        NeuesLog().Schreibe(LogStufe.Fehler, "Start", "Start fehlgeschlagen", ausnahme: gefangen);

        var zeile = Assert.Single(Zeilen(Datei("agent.log")));
        Assert.Equal("FEHLER", zeile.GetProperty("stufe").GetString());
        var ausnahme = zeile.GetProperty("ausnahme").GetString()!;
        Assert.Contains("System.InvalidOperationException: kaputt", ausnahme);
        Assert.Contains(nameof(Ausnahme_WirdMitTypUndStacktraceGeschrieben), ausnahme);
    }

    [Fact]
    public void Debug_IstStandardmaessigAus_UndLaesstSichEinschalten()
    {
        NeuesLog().Schreibe(LogStufe.Debug, "Netz", "unsichtbar");
        Assert.False(File.Exists(Datei("agent.log")));

        NeuesLog(o => o with { MinStufe = LogStufe.Debug }).Schreibe(LogStufe.Debug, "Netz", "sichtbar");
        Assert.Equal("DEBUG", Assert.Single(Zeilen(Datei("agent.log"))).GetProperty("stufe").GetString());
    }

    [Fact]
    public void StufeWarnung_VerwirftInfo()
    {
        var log = NeuesLog(o => o with { MinStufe = LogStufe.Warnung });
        log.Schreibe(LogStufe.Info, "Netz", "weg");
        log.Schreibe(LogStufe.Warnung, "Netz", "da");

        Assert.Equal("WARNUNG", Assert.Single(Zeilen(Datei("agent.log"))).GetProperty("stufe").GetString());
    }

    [Theory]
    [InlineData("{\"Stufe\":\"DEBUG\"}", LogStufe.Debug)]
    [InlineData("{\"Stufe\":\"debug\"}", LogStufe.Debug)]
    [InlineData("{\"Stufe\":\"Warnung\"}", LogStufe.Warnung)]
    [InlineData("{\"Stufe\":\"FEHLER\"}", LogStufe.Fehler)]
    [InlineData("{\"Stufe\":\"INFO\"}", LogStufe.Info)]
    public void LeseStufe_AusDatei(string inhalt, LogStufe erwartet)
    {
        var pfad = Datei("betriebslog.json");
        File.WriteAllText(pfad, inhalt);

        Assert.Equal(erwartet, BetriebsLog.LeseStufe(pfad, out var hinweis));
        Assert.Null(hinweis);
    }

    [Fact]
    public void LeseStufe_OhneDatei_Info_OhneHinweis()
    {
        Assert.Equal(LogStufe.Info, BetriebsLog.LeseStufe(Datei("fehlt.json"), out var hinweis));
        Assert.Null(hinweis);
    }

    [Theory]
    [InlineData("{\"Stufe\":\"laut\"}")]
    [InlineData("{\"Stufe\":3}")]
    [InlineData("{}")]
    [InlineData("kein json")]
    public void LeseStufe_Ungueltig_Info_MitHinweisAufErlaubteWerteOderDatei(string inhalt)
    {
        var pfad = Datei("betriebslog.json");
        File.WriteAllText(pfad, inhalt);

        Assert.Equal(LogStufe.Info, BetriebsLog.LeseStufe(pfad, out var hinweis));
        Assert.NotNull(hinweis);
        Assert.Contains(pfad, hinweis);
    }

    [Fact]
    public void Tageswechsel_BenenntDieDateiMitDemDatumDerEintraegeUm()
    {
        var log = NeuesLog();
        var gestern = HeuteText;
        log.Schreibe(LogStufe.Info, "Test", "gestern");

        _zeit.Jetzt = _zeit.Jetzt.AddDays(1);
        log.Schreibe(LogStufe.Info, "Test", "heute");

        Assert.Equal("gestern", Assert.Single(Zeilen(Datei($"agent-{gestern}.log"))).GetProperty("meldung").GetString());
        Assert.Equal("heute", Assert.Single(Zeilen(Datei("agent.log"))).GetProperty("meldung").GetString());
    }

    [Fact]
    public void Start_MitDateiVomVortag_BenenntSieVorDemErstenEintragUm()
    {
        var alt = Datei("agent.log");
        File.WriteAllText(alt, "{\"meldung\":\"vom Vortag\"}\n");
        var vortag = _zeit.GetLocalNow().AddDays(-1);
        File.SetLastWriteTime(alt, vortag.DateTime);

        NeuesLog().Schreibe(LogStufe.Info, "Test", "heute");

        Assert.Equal("vom Vortag", Assert.Single(Zeilen(Datei($"agent-{vortag:yyyy-MM-dd}.log"))).GetProperty("meldung").GetString());
        Assert.Equal("heute", Assert.Single(Zeilen(alt)).GetProperty("meldung").GetString());
    }

    [Fact]
    public void Start_AmSelbenTag_SchreibtInDieVorhandeneDateiWeiter()
    {
        NeuesLog().Schreibe(LogStufe.Info, "Test", "erster Prozess");
        NeuesLog().Schreibe(LogStufe.Info, "Test", "zweiter Prozess");

        Assert.Equal(2, Zeilen(Datei("agent.log")).Count);
        Assert.Single(Directory.GetFiles(_ordner));
    }

    [Fact]
    public void Notbremse_VolleDatei_WarnungUndWeiterInDerNaechsten()
    {
        var log = NeuesLog(o => o with { MaxDateiBytes = 600 });
        for (var i = 0; i < 6; i++)
        {
            log.Schreibe(LogStufe.Info, "Test", $"Eintrag {i} " + new string('x', 60));
        }

        Assert.True(new FileInfo(Datei("agent.log")).Length <= 600);
        var weiter = Zeilen(Datei("agent.1.log"));
        Assert.Equal("WARNUNG", weiter[0].GetProperty("stufe").GetString());
        Assert.Contains("agent.1.log", weiter[0].GetProperty("meldung").GetString());
        var alle = Directory.GetFiles(_ordner, "agent*.log").SelectMany(Zeilen).ToList();
        Assert.Equal(6, alle.Count(z => z.GetProperty("modul").GetString() == "Test"));
        Assert.All(Directory.GetFiles(_ordner, "agent*.log"), d => Assert.True(new FileInfo(d).Length <= 600, d));
    }

    [Fact]
    public void Notbremse_VolleDatei_TeilDateienWerdenBeimTageswechselMitUmbenannt()
    {
        var log = NeuesLog(o => o with { MaxDateiBytes = 300 });
        var gestern = HeuteText;
        for (var i = 0; i < 4; i++)
        {
            log.Schreibe(LogStufe.Info, "Test", new string('x', 100));
        }

        _zeit.Jetzt = _zeit.Jetzt.AddDays(1);
        log.Schreibe(LogStufe.Info, "Test", "heute");

        Assert.True(File.Exists(Datei($"agent-{gestern}.log")));
        Assert.True(File.Exists(Datei($"agent-{gestern}.1.log")));
        Assert.False(File.Exists(Datei("agent.1.log")));
        Assert.Equal("heute", Assert.Single(Zeilen(Datei("agent.log"))).GetProperty("meldung").GetString());
    }

    [Fact]
    public void Notbremse_Gesamtgrenze_LoeschtDieAeltestenDateienZuerstUndWarnt()
    {
        var heute = DateOnly.FromDateTime(_zeit.GetLocalNow().DateTime);
        var aeltest = Datei($"agent-{heute.AddDays(-3):yyyy-MM-dd}.log");
        var mittel = Datei($"agent-{heute.AddDays(-2):yyyy-MM-dd}.log");
        var juengst = Datei($"agent-{heute.AddDays(-1):yyyy-MM-dd}.log");
        foreach (var pfad in new[] { aeltest, mittel, juengst })
        {
            File.WriteAllText(pfad, new string('x', 400));
        }

        var fremd = Datei("config-2020-01-01.log");
        File.WriteAllText(fremd, new string('x', 4000));

        var log = NeuesLog(o => o with { MaxGesamtBytes = 1000 });
        log.Schreibe(LogStufe.Info, "Test", "heute");
        Assert.True(log.Aufraeumen.Wait(TimeSpan.FromSeconds(10)), "Aufräumen nicht nach 10 s fertig.");

        Assert.False(File.Exists(aeltest));
        Assert.True(File.Exists(mittel));
        Assert.True(File.Exists(juengst));
        Assert.True(File.Exists(fremd), "Dateien eines anderen Prozesses zählen nicht mit und bleiben.");
        var warnung = Zeilen(Datei("agent.log")).First(z => z.GetProperty("stufe").GetString() == "WARNUNG");
        Assert.Contains(Path.GetFileName(aeltest), warnung.GetProperty("meldung").GetString());
    }

    [Fact]
    public void Aufbewahrung_LoeschtArchiveNach90Tagen_KomprimiertAb7Tagen()
    {
        var heute = DateOnly.FromDateTime(_zeit.GetLocalNow().DateTime);
        var zuAlt = Datei($"agent-{heute.AddDays(-91):yyyy-MM-dd}.log");
        var grenzeAufbewahrung = Datei($"agent-{heute.AddDays(-90):yyyy-MM-dd}.log.gz");
        var komprimieren = Datei($"agent-{heute.AddDays(-7):yyyy-MM-dd}.log");
        var komprimierenTeil = Datei($"agent-{heute.AddDays(-8):yyyy-MM-dd}.1.log");
        var frisch = Datei($"agent-{heute.AddDays(-6):yyyy-MM-dd}.log");
        File.WriteAllText(zuAlt, "alt\n");
        File.WriteAllText(grenzeAufbewahrung, string.Empty);
        File.WriteAllText(komprimieren, "{\"meldung\":\"vor einer Woche\"}\n");
        File.WriteAllText(komprimierenTeil, "teil\n");
        File.WriteAllText(frisch, "frisch\n");

        var log = NeuesLog();
        log.Schreibe(LogStufe.Info, "Test", "heute");
        Assert.True(log.Aufraeumen.Wait(TimeSpan.FromSeconds(10)), "Aufräumen nicht nach 10 s fertig.");

        Assert.False(File.Exists(zuAlt));
        Assert.True(File.Exists(grenzeAufbewahrung));
        Assert.False(File.Exists(komprimieren));
        Assert.False(File.Exists(komprimierenTeil));
        Assert.True(File.Exists(komprimierenTeil + ".gz"));
        Assert.True(File.Exists(frisch));
        using var gz = new GZipStream(File.OpenRead(komprimieren + ".gz"), CompressionMode.Decompress);
        using var leser = new StreamReader(gz);
        Assert.Equal("{\"meldung\":\"vor einer Woche\"}\n", leser.ReadToEnd());
    }

    [Fact]
    public void Umlaute_BleibenLesbar()
    {
        NeuesLog().Schreibe(LogStufe.Info, "Lizenz", "Prüfung gültig");

        Assert.Contains("Prüfung gültig", File.ReadAllText(Datei("agent.log")));
    }

    [Fact]
    public void FremdeDateiMitRiesigerTeilnummer_WirdIgnoriert()
    {
        File.WriteAllText(Datei("agent.99999999999.log"), "fremd\n");

        NeuesLog().Schreibe(LogStufe.Info, "Test", "trotzdem");

        Assert.Equal("trotzdem", Assert.Single(Zeilen(Datei("agent.log"))).GetProperty("meldung").GetString());
    }

    [Fact]
    public void ZweiterAgent_NachTageswechselDesErsten_BenenntDieNeueDateiNichtAufDenVortagUm()
    {
        var vortag = HeuteText;
        var a = NeuesLog();
        var b = NeuesLog();
        a.Schreibe(LogStufe.Info, "Test", "a gestern");
        b.Schreibe(LogStufe.Info, "Test", "b gestern");
        File.SetLastWriteTime(Datei("agent.log"), _zeit.GetLocalNow().DateTime);

        _zeit.Jetzt = _zeit.Jetzt.AddDays(1);
        a.Schreibe(LogStufe.Info, "Test", "a heute");
        File.SetLastWriteTime(Datei("agent.log"), _zeit.GetLocalNow().DateTime);
        b.Schreibe(LogStufe.Info, "Test", "b heute");

        Assert.Equal(["a gestern", "b gestern"], Zeilen(Datei($"agent-{vortag}.log")).Select(z => z.GetProperty("meldung").GetString()));
        Assert.Equal(["a heute", "b heute"], Zeilen(Datei("agent.log")).Select(z => z.GetProperty("meldung").GetString()));
    }

    [Fact]
    public void ZweiProzesse_DateigrenzeGiltFuerDieGemeinsameDatei()
    {
        var a = NeuesLog(o => o with { MaxDateiBytes = 600 });
        var b = NeuesLog(o => o with { MaxDateiBytes = 600 });
        for (var i = 0; i < 4; i++)
        {
            a.Schreibe(LogStufe.Info, "Test", new string('a', 100));
            b.Schreibe(LogStufe.Info, "Test", new string('b', 100));
        }

        Assert.True(new FileInfo(Datei("agent.log")).Length <= 600);
        Assert.True(File.Exists(Datei("agent.1.log")));
    }

    [Fact]
    public void ParallelVonMehrerenThreads_KeineZeileGehtVerloren()
    {
        var log = NeuesLog();
        Parallel.For(0, 200, i => log.Schreibe(LogStufe.Info, "Test", $"Zeile {i}"));

        Assert.Equal(200, Zeilen(Datei("agent.log")).Count);
    }

    private sealed class TestZeit(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Jetzt { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Jetzt.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
    }
}
