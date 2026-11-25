using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Diagnostics;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using Xunit;

namespace HaelpMi.Core.Tests;

// Eigene Collection: einige Tests tauschen FremdeKundengruppeMelder.Prozess, das ist prozessweit.
[Collection(FremdeKundengruppeCollection.Name)]
public sealed class FremdeKundengruppeMelderTests : IDisposable
{
    private readonly TestZeit _zeit = new(new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero));
    private readonly List<(string Meldung, string? Objekt)> _warnungen = [];
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "HaelpMiFremdeGruppe_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_ordner))
            {
                Directory.Delete(_ordner, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // nur Aufräumen des Temp-Ordners
        }
    }

    private FremdeKundengruppeMelder NeuerMelder(TimeSpan? intervall = null, int max = FremdeKundengruppeMelder.StandardMaxKombinationen) =>
        new(intervall, (meldung, objekt) => _warnungen.Add((meldung, objekt)), _zeit, max);

    [Fact]
    public void ErsteNachricht_SchreibtSofortEineWarnungMitArtGekuerzterGruppeUndAbsender()
    {
        var melder = NeuerMelder();
        var gruppe = Guid.NewGuid();
        var absender = Guid.NewGuid();

        melder.Verworfen("Alarm", gruppe, absender);

        var (meldung, objekt) = Assert.Single(_warnungen);
        Assert.Contains("Art Alarm", meldung);
        Assert.Contains(gruppe.ToString("N")[..8], meldung);
        Assert.DoesNotContain(gruppe.ToString("N"), meldung);
        Assert.DoesNotContain(gruppe.ToString(), meldung);
        Assert.Contains("Anzahl seit der letzten Meldung: 1", meldung);
        Assert.Equal(absender.ToString(), objekt);
    }

    [Fact]
    public void Wiederholungen_ImIntervall_WerdenGedrosseltUndInDerNaechstenMeldungGezaehlt()
    {
        var melder = NeuerMelder(TimeSpan.FromMinutes(60));
        var gruppe = Guid.NewGuid();

        melder.Verworfen("Alarm", gruppe, null);
        for (var i = 0; i < 4; i++)
        {
            _zeit.Jetzt += TimeSpan.FromMinutes(10);
            melder.Verworfen("Alarm", gruppe, null);
        }

        Assert.Single(_warnungen);

        _zeit.Jetzt += TimeSpan.FromMinutes(20);
        melder.Verworfen("Alarm", gruppe, null);

        Assert.Equal(2, _warnungen.Count);
        Assert.Contains("Anzahl seit der letzten Meldung: 5", _warnungen[1].Meldung);

        melder.Verworfen("Alarm", gruppe, null);
        Assert.Equal(2, _warnungen.Count);
    }

    [Fact]
    public void AndereArtOderAndereGruppe_WirdGetrenntGemeldet()
    {
        var melder = NeuerMelder();
        var gruppe = Guid.NewGuid();

        melder.Verworfen("Alarm", gruppe, null);
        melder.Verworfen("Boot-Call", gruppe, null);
        melder.Verworfen("Alarm", Guid.NewGuid(), null);
        melder.Verworfen("Alarm", gruppe, null);

        Assert.Equal(3, _warnungen.Count);
    }

    [Fact]
    public void VieleNeueGruppen_UeberDerObergrenze_ErgebenNurEineSammelmeldung()
    {
        var melder = NeuerMelder(max: 2);

        for (var i = 0; i < 50; i++)
        {
            melder.Verworfen("Alarm", Guid.NewGuid(), null);
        }

        Assert.Equal(3, _warnungen.Count);
        Assert.Contains("weiterer fremder Kundengruppen", _warnungen[2].Meldung);

        _zeit.Jetzt += FremdeKundengruppeMelder.StandardIntervall;
        melder.Verworfen("Alarm", Guid.NewGuid(), null);

        // Nach Ablauf des Intervalls werden alte Kombinationen frei, die neue Gruppe bekommt ihre eigene Meldung.
        Assert.Equal(4, _warnungen.Count);
        Assert.Contains("Art Alarm", _warnungen[3].Meldung);
    }

    [Fact]
    public void Warnung_LandetAlsWarnungImBetriebsLog_OhneVolleGruppenId()
    {
        Directory.CreateDirectory(_ordner);
        var log = new BetriebsLog(new BetriebsLogOptionen(_ordner, "agent"));
        var melder = new FremdeKundengruppeMelder(
            schreibeWarnung: (meldung, objekt) => log.Schreibe(LogStufe.Warnung, FremdeKundengruppeMelder.Modul, meldung, objekt));
        var gruppe = Guid.NewGuid();

        melder.Verworfen("Config-Abruf", gruppe, Guid.NewGuid());

        Assert.True(log.Aufraeumen.Wait(TimeSpan.FromSeconds(10)));
        var zeile = Assert.Single(File.ReadAllLines(Path.Combine(_ordner, "agent.log")));
        Assert.Contains("\"stufe\":\"WARNUNG\"", zeile);
        Assert.Contains("\"modul\":\"Kundengruppe\"", zeile);
        Assert.DoesNotContain(gruppe.ToString(), zeile);
        Assert.DoesNotContain(gruppe.ToString("N"), zeile);
    }

    [Fact]
    public void Filter_FremdeGruppe_MeldetUeberDenProzessMelder_EigeneGruppeNicht()
    {
        var vorher = FremdeKundengruppeMelder.Prozess;
        FremdeKundengruppeMelder.Prozess = NeuerMelder();
        try
        {
            var eigene = Guid.NewGuid();

            Assert.True(CustomerGroupFilter.Matches(eigene, eigene, "Alarm", Guid.NewGuid()));
            Assert.Empty(_warnungen);

            Assert.False(CustomerGroupFilter.Matches(Guid.NewGuid(), eigene, "Alarm", Guid.NewGuid()));
            Assert.Single(_warnungen);
        }
        finally
        {
            FremdeKundengruppeMelder.Prozess = vorher;
        }
    }

    [Fact]
    public async Task AlarmTcpListener_FremdeGruppe_SchreibtWarnungInsBetriebsLogUndNichtsInsAuditLog()
    {
        var vorher = FremdeKundengruppeMelder.Prozess;
        FremdeKundengruppeMelder.Prozess = NeuerMelder();
        var empfaenger = Guid.NewGuid();
        var identitaet = new LiveIdentity(Guid.NewGuid(), empfaenger, "PC", "User", "Raum", "1", Role.User, false, "9.9.9", 0, DateTimeOffset.UtcNow);
        var audit = new List<string>();
        var listener = new AlarmTcpListener(() => identitaet, audit.Add);
        var port = FreierTcpPort();
        listener.Start(port);
        try
        {
            var fremdeGruppe = Guid.NewGuid();
            var fremderAbsender = Guid.NewGuid();
            var fremd = new LiveIdentity(fremdeGruppe, fremderAbsender, "PC-Fremd", "X", "Y", "2", Role.User, false, "9.9.9", 0, DateTimeOffset.UtcNow);
            var ziel = new DeviceEntry { DeviceId = empfaenger, IpAddress = "127.0.0.1", TcpPort = port };

            var ergebnis = await new AlarmSender().SendAsync(new AlarmProfile { Text = "Geheimer Alarmtext" }, Guid.NewGuid(), fremd, new[] { ziel });

            Assert.Equal(0, ergebnis.AckedCount);
            var (meldung, objekt) = Assert.Single(_warnungen);
            Assert.Contains("Art Alarm", meldung);
            Assert.Contains(FremdeKundengruppeMelder.Maskiere(fremdeGruppe), meldung);
            Assert.DoesNotContain("Geheimer Alarmtext", meldung);
            Assert.Equal(fremderAbsender.ToString(), objekt);
            Assert.Empty(audit);
        }
        finally
        {
            await listener.DisposeAsync();
            FremdeKundengruppeMelder.Prozess = vorher;
        }
    }

    private static int FreierTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Theory]
    [InlineData(null, 60, false)]
    [InlineData("{\"Stufe\":\"DEBUG\"}", 60, false)]
    [InlineData("{\"FremdeKundengruppeDrosselMinuten\":15}", 15, false)]
    [InlineData("{\"FremdeKundengruppeDrosselMinuten\":0}", 60, true)]
    [InlineData("{\"FremdeKundengruppeDrosselMinuten\":\"abc\"}", 60, true)]
    [InlineData("kein json", 60, true)]
    public void LeseIntervall_AusBetriebslogJson(string? inhalt, int erwarteteMinuten, bool hinweisErwartet)
    {
        Directory.CreateDirectory(_ordner);
        var pfad = Path.Combine(_ordner, "betriebslog.json");
        if (inhalt is not null)
        {
            File.WriteAllText(pfad, inhalt);
        }

        var intervall = FremdeKundengruppeMelder.LeseIntervall(pfad, out var hinweis);

        Assert.Equal(TimeSpan.FromMinutes(erwarteteMinuten), intervall);
        Assert.Equal(hinweisErwartet, hinweis is not null);
    }

    private sealed class TestZeit(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Jetzt { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Jetzt;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Jetzt.UtcTicks;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FremdeKundengruppeCollection
{
    public const string Name = "FremdeKundengruppe";
}
