using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using HaelpMi.Core.Autostart;
using HaelpMi.Core.Models;
using Xunit;

namespace HaelpMi.E2E.Tests;

/// <summary>Findet und bedient die installierte HälpMi-App (Agent + Konfigurationsprogramm) über UI Automation.</summary>
internal sealed class HaelpMiApp : IDisposable
{
    public const string AgentProzess = "HaelpMi.Agent";
    public const string KonfigProzess = "HaelpMi.Config";

    public const string TitelEinstellungen = "HälpMi - Einstellungen";
    public const string TitelDashboard = "HälpMi - Admin-Dashboard";
    public const string AlarmfensterId = "AlarmPopupWindow";
    public const string SenderBannerId = "SenderStatusWindow";

    public UIA3Automation Automation { get; } = new();

    public static string KonfigExe => Path.Combine(E2EUmgebung.ProgrammOrdner, KonfigProzess + ".exe");

    /// <summary>
    /// [Icons] in HaelpMiCommon.iss.inc: Admin bekommt "HälpMi Dashboard" nur im Startmenü des
    /// installierenden Nutzers ({userprograms}), User "HälpMi Konfiguration" im gemeinsamen ({group}).
    /// </summary>
    public static string VerknuepfungFuer(Role rolle) => rolle == Role.Admin
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "HälpMi", "HälpMi Dashboard.lnk")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "HälpMi", "HälpMi Konfiguration.lnk");

    public static string FenstertitelFuerVerknuepfung(Role rolle) => rolle == Role.Admin ? TitelDashboard : TitelEinstellungen;

    /// <summary>Wartet auf einen Agent in der Sitzung des Testlaufs; einer in einer fremden Sitzung zählt nicht.</summary>
    public static async Task WarteAufAgentAsync(TimeSpan timeout)
    {
        var frist = DateTime.UtcNow + timeout;
        while (!Prozess.LaeuftInEigenerSitzung(AgentProzess))
        {
            if (DateTime.UtcNow > frist)
            {
                throw new TimeoutException($"{AgentProzess} läuft nach {timeout} nicht in Sitzung {Prozess.EigeneSitzung}.");
            }

            await Task.Delay(500);
        }
    }

    /// <summary>Über die geplante Aufgabe statt direkt: der Runner läuft erhöht, der Agent soll wie beim Nutzer ohne Adminrechte laufen.</summary>
    public static void StarteAgentUeberAutostart()
    {
        var ergebnis = Prozess.Ausfuehren("schtasks.exe", $"/Run /TN \"{AutostartRegistrar.TaskName}\"");
        if (ergebnis.ExitCode != 0)
        {
            throw new InvalidOperationException($"schtasks /Run für '{AutostartRegistrar.TaskName}' fehlgeschlagen: {ergebnis.Ausgabe}");
        }
    }

    public static void BeendeKonfiguration() => Prozess.Beenden(KonfigProzess);

    public static void BeendeAlles()
    {
        Prozess.Beenden(KonfigProzess);
        Prozess.Beenden(AgentProzess);
    }

    public Window OeffneUeberVerknuepfung(Role rolle, TimeSpan timeout)
    {
        var lnk = VerknuepfungFuer(rolle);
        if (!File.Exists(lnk))
        {
            throw new FileNotFoundException("Startmenü-Verknüpfung fehlt.", lnk);
        }

        Process.Start(new ProcessStartInfo(lnk) { UseShellExecute = true })?.Dispose();
        return WarteAufFenster(KonfigProzess, FenstertitelFuerVerknuepfung(rolle), null, timeout);
    }

    /// <summary>Konfigurationsprogramm ohne --open-dashboard, also immer das Einstellungsfenster (auch auf einem Admin-Gerät).</summary>
    public Window OeffneEinstellungen(TimeSpan timeout)
    {
        Process.Start(new ProcessStartInfo(KonfigExe) { UseShellExecute = true })?.Dispose();
        return WarteAufFenster(KonfigProzess, TitelEinstellungen, null, timeout);
    }

    public Window WarteAufAlarmfenster(TimeSpan timeout) => WarteAuf(FindeAlarmfenster, "Alarmfenster", timeout);

    public Window WarteAufSenderBanner(TimeSpan timeout) => WarteAuf(FindeSenderBanner, "Sender-Banner", timeout);

    /// <summary>Wie <see cref="WarteAufAlarmfenster"/>, aber ein Ausbleiben ist ein Testfehler mit <paramref name="fall"/> in der Meldung.</summary>
    public Window ErwartePopup(string fall) => Erwarte(FindeAlarmfenster, $"{fall}: kein Alarmfenster", TimeSpan.FromSeconds(15));

    public Window ErwarteBanner(string fall) =>
        Erwarte(FindeSenderBanner, $"{fall}: kein Sender-Banner nach dem Hotkey (Hotkey nicht registriert oder kein gültiger Empfänger)", TimeSpan.FromSeconds(10));

    private Window Erwarte(Func<Window?> suche, string meldung, TimeSpan timeout)
    {
        try
        {
            return WarteAuf(suche, meldung, timeout);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"{meldung} nach {timeout.TotalSeconds:0} s.");
            throw;
        }
    }

    public bool AlarmfensterOffen() => FindeAlarmfenster() is not null;

    // Fenster-AutomationIds gibt es erst seit v0.47.x; ältere Stände (Vergleich mit v0.46.1) erkennt man an den Kindern.
    public Window? FindeAlarmfenster() =>
        FindeFenster(AgentProzess, null, AlarmfensterId) ?? FindeFensterMitKind(AgentProzess, "OnMyWayButton");

    public Window? FindeSenderBanner() =>
        FindeFenster(AgentProzess, null, SenderBannerId) ?? FindeFensterMitKind(AgentProzess, "CancelButton", "AckedText");

    /// <summary>Schließt alle offenen Sender-Banner, z. B. aus einem vorherigen Test.</summary>
    public async Task SchliesseSenderBannerAsync()
    {
        for (var i = 0; i < 10 && FindeSenderBanner() is { } banner; i++)
        {
            try
            {
                banner.Close();
            }
            catch (COMException)
            {
            }

            await Task.Delay(500);
        }
    }

    /// <summary>Negativprüfung: über die ganze <paramref name="dauer"/> darf <paramref name="suche"/> kein Fenster finden.</summary>
    public static async Task WarteKeinFensterAsync(Func<Window?> suche, TimeSpan dauer, string meldung)
    {
        var ende = DateTime.UtcNow + dauer;
        while (DateTime.UtcNow < ende)
        {
            if (suche() is not null)
            {
                Assert.Fail(meldung);
            }

            await Task.Delay(300);
        }
    }

    /// <summary>Wie <see cref="WarteKeinFensterAsync"/>, aber bis die Marke da ist und danach noch <paramref name="nachlauf"/>.</summary>
    public static async Task KeinFensterBisMarkeAsync(Func<Window?> suche, string marke, TimeSpan timeout, TimeSpan nachlauf, string meldung)
    {
        var markeDa = Rendezvous.WaitForAsync(marke, timeout);
        while (!markeDa.IsCompleted)
        {
            if (suche() is not null)
            {
                Assert.Fail(meldung);
            }

            await Task.WhenAny(markeDa, Task.Delay(300));
        }

        await markeDa;
        await WarteKeinFensterAsync(suche, nachlauf, meldung);
    }

    /// <summary>Agent beenden und über den Autostart neu starten.</summary>
    /// <returns>PID des neuen Agent-Prozesses.</returns>
    public static async Task<int> AgentNeustartAsync()
    {
        var alt = AgentPid();
        BeendeAlles();
        StarteAgentUeberAutostart();
        await WarteAufAgentAsync(TimeSpan.FromSeconds(60));
        var neu = AgentPid() ?? throw new InvalidOperationException($"{AgentProzess} läuft nach dem Neustart nicht.");
        Assert.NotEqual(alt, neu);
        return neu;
    }

    public static int? AgentPid()
    {
        var prozesse = Process.GetProcessesByName(AgentProzess);
        var pid = prozesse.FirstOrDefault(p => p.SessionId == Prozess.EigeneSitzung)?.Id;
        foreach (var p in prozesse) p.Dispose();
        return pid;
    }

    /// <summary>Einträge aus audit.log (alle <see cref="E2EUmgebung.LogOrdner"/>) mit Zeitstempel ab <paramref name="seit"/>.</summary>
    public static List<string> AuditLogZeilen(DateTimeOffset seit)
    {
        var zeilen = new List<string>();
        foreach (var ordner in E2EUmgebung.LogOrdner())
        {
            var datei = Path.Combine(ordner, "audit.log");
            if (!File.Exists(datei))
            {
                continue;
            }

            try
            {
                using var stream = new FileStream(datei, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var leser = new StreamReader(stream);
                while (leser.ReadLine() is { } zeile)
                {
                    var tab = zeile.IndexOf('\t');
                    if (tab > 0
                        && DateTimeOffset.TryParse(zeile[..tab], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var zeit)
                        && zeit >= seit)
                    {
                        zeilen.Add(zeile);
                    }
                }
            }
            catch (IOException)
            {
            }
        }

        return zeilen;
    }

    /// <summary>Erstes Fenster des Prozesses, das alle genannten Kind-AutomationIds enthält; die Fenster-AutomationId spielt keine Rolle.</summary>
    public Window? FindeFensterMitKind(string prozessName, params string[] kindAutomationIds)
    {
        foreach (var fenster in FensterVon(prozessName))
        {
            try
            {
                if (kindAutomationIds.All(id => fenster.FindFirstDescendant(cf => cf.ByAutomationId(id)) is not null))
                {
                    return fenster.AsWindow();
                }
            }
            catch (COMException)
            {
            }
        }

        return null;
    }

    /// <summary>Standard-Dialog (MessageBox, Fensterklasse #32770) des Prozesses.</summary>
    public Window? FindeDialog(string prozessName)
    {
        foreach (var fenster in FensterVon(prozessName))
        {
            try
            {
                if (fenster.Properties.ClassName.ValueOrDefault == "#32770")
                {
                    return fenster.AsWindow();
                }
            }
            catch (COMException)
            {
            }
        }

        return null;
    }

    public Window WarteAuf(Func<Window?> suche, string beschreibung, TimeSpan timeout)
    {
        var frist = DateTime.UtcNow + timeout;
        while (true)
        {
            if (suche() is { } fenster)
            {
                return fenster;
            }

            if (DateTime.UtcNow > frist)
            {
                throw new TimeoutException($"{beschreibung} ist nach {timeout} nicht erschienen.");
            }

            Thread.Sleep(300);
        }
    }

    /// <param name="titel">Fenstertitel (UIA-Name) oder null.</param>
    /// <param name="automationId">AutomationId des Fensters oder null.</param>
    public Window WarteAufFenster(string prozessName, string? titel, string? automationId, TimeSpan timeout) =>
        WarteAuf(() => FindeFenster(prozessName, titel, automationId), $"Fenster '{titel ?? automationId}' von {prozessName}", timeout);

    public static bool IstImVordergrund(Window fenster) =>
        fenster.Properties.NativeWindowHandle.ValueOrDefault == GetForegroundWindow();

    /// <summary>Drückt ein HälpMi-Tastenkürzel systemweit, wie ein Nutzer es auf der Tastatur täte.</summary>
    public static void DrueckeHotkey(HotkeyDefinition hotkey)
    {
        var tasten = new List<VirtualKeyShort>();
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control)) tasten.Add(VirtualKeyShort.CONTROL);
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) tasten.Add(VirtualKeyShort.ALT);
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) tasten.Add(VirtualKeyShort.SHIFT);
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win)) tasten.Add(VirtualKeyShort.LWIN);
        tasten.Add((VirtualKeyShort)hotkey.VirtualKeyCode);
        Keyboard.TypeSimultaneously(tasten.ToArray());
    }

    public static AutomationElement Element(AutomationElement wurzel, string automationId) =>
        wurzel.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
        ?? throw new InvalidOperationException($"Element '{automationId}' nicht gefunden.");

    public static Task WarteBisAsync(Func<bool> bedingung, TimeSpan timeout, string meldung) =>
        WarteBisAsync(bedingung, timeout, () => meldung);

    /// <param name="meldung">Erst beim Fehlschlag ausgewertet, zeigt also den Zustand nach Ablauf der Frist.</param>
    public static async Task WarteBisAsync(Func<bool> bedingung, TimeSpan timeout, Func<string> meldung)
    {
        var frist = DateTime.UtcNow + timeout;
        while (!Gilt(bedingung))
        {
            if (DateTime.UtcNow > frist)
            {
                Assert.Fail(meldung());
            }

            await Task.Delay(300);
        }
    }

    /// <summary>Name (bei Textelementen der angezeigte Text) eines Kind-Elements; leer, wenn es (noch) nicht da ist.</summary>
    public static string NameVon(AutomationElement wurzel, string automationId)
    {
        try
        {
            return wurzel.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.Properties.Name.ValueOrDefault ?? string.Empty;
        }
        catch (COMException)
        {
            return string.Empty;
        }
    }

    /// <summary>"Bin unterwegs", warten bis Schließen frei wird, schließen.</summary>
    public async Task QuittiereUndSchliesseAsync(Window popup, string wer)
    {
        Element(popup, "OnMyWayButton").AsButton().Invoke();
        var schliessen = Element(popup, "CloseButton").AsButton();
        await WarteBisAsync(() => schliessen.IsEnabled, TimeSpan.FromSeconds(15),
            () => $"{wer}: Schließen wird nach 'Bin unterwegs' nicht aktiv ({NameVon(popup, "ThresholdStatusText")}).");
        schliessen.Invoke();
        await WarteBisAsync(() => !AlarmfensterOffen(), TimeSpan.FromSeconds(10), $"{wer}: Alarmfenster ist nach dem Schließen noch offen.");
    }

    /// <summary>
    /// Prüfung, deren Fehlschlag ein Produktbefund ist: der Test läuft weiter und prüft den Rest,
    /// <see cref="MeldeBefunde"/> lässt ihn am Ende trotzdem scheitern.
    /// </summary>
    public static async Task PruefeBefundAsync(List<string> befunde, Func<Task> pruefung)
    {
        try
        {
            await pruefung();
        }
        catch (Xunit.Sdk.XunitException ex)
        {
            befunde.Add(ex.Message);
        }
    }

    public static void MeldeBefunde(List<string> befunde) =>
        Assert.True(befunde.Count == 0, string.Join(Environment.NewLine, befunde.Prepend($"{befunde.Count} Produktbefund(e):")));

    public void Dispose() => Automation.Dispose();

    // Ein Fenster kann zwischen Suchen und Abfragen verschwinden; das zählt als "noch nicht".
    private static bool Gilt(Func<bool> bedingung)
    {
        try
        {
            return bedingung();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false;
        }
    }

    public Window? FindeFenster(string prozessName, string? titel, string? automationId)
    {
        foreach (var element in FensterVon(prozessName))
        {
            try
            {
                if (titel is not null && element.Properties.Name.ValueOrDefault != titel)
                {
                    continue;
                }

                if (automationId is not null && element.Properties.AutomationId.ValueOrDefault != automationId)
                {
                    continue;
                }

                return element.AsWindow();
            }
            catch (COMException)
            {
                // Fenster wurde zwischen Aufzählen und Abfragen geschlossen.
            }
        }

        return null;
    }

    private List<AutomationElement> FensterVon(string prozessName)
    {
        var pids = Process.GetProcessesByName(prozessName).Select(p => { using (p) return p.Id; }).ToHashSet();
        var treffer = new List<AutomationElement>();
        if (pids.Count == 0)
        {
            return treffer;
        }

        foreach (var element in Automation.GetDesktop().FindAllChildren())
        {
            try
            {
                if (pids.Contains(element.Properties.ProcessId.ValueOrDefault))
                {
                    treffer.Add(element);
                }
            }
            catch (COMException)
            {
            }
        }

        return treffer;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
