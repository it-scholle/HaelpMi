using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;
using Xunit;
using static HaelpMi.E2E.Tests.HaelpMiApp;

namespace HaelpMi.E2E.Tests;

/// <summary>
/// Bedient das Admin-Dashboard so, wie ein Admin es tut. Zwei Eigenheiten der Oberfläche bestimmen den Weg:
/// Textfelder speichern erst im LostFocus, und die Sender-/Empfänger-/Geräte-Listen reagieren nur auf
/// PreviewMouseLeftButtonUp (eine Auswahl per UIA ändert dort nichts) - deshalb echte Tastatur- und Mausereignisse.
/// </summary>
internal sealed class DashboardSeite
{
    public const string Gespeichert = "Gespeichert und an alle Geräte verteilt.";

    private static readonly TimeSpan FensterTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SpeicherTimeout = TimeSpan.FromSeconds(15);

    private DashboardSeite(Window fenster) => Fenster = fenster;

    public Window Fenster { get; }

    public static DashboardSeite Oeffne(HaelpMiApp app)
    {
        BeendeKonfiguration();
        var fenster = app.OeffneUeberVerknuepfung(Role.Admin, FensterTimeout);

        // Maximiert passen die Listen ohne äußeres Scrollen auf den Bildschirm, die Mausklicks treffen sicher.
        fenster.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized);
        fenster.SetForeground();
        Thread.Sleep(500);
        return new DashboardSeite(fenster);
    }

    public AutomationElement Element(string automationId) => HaelpMiApp.Element(Fenster, automationId);

    public void WaehleTab(string kopf)
    {
        var tab = Fenster.FindFirstDescendant(cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName(kopf)))
            ?? throw new InvalidOperationException($"Reiter '{kopf}' nicht gefunden.");
        tab.AsTabItem().Select();
        Thread.Sleep(500);
    }

    public void WaehleTabPerId(string automationId)
    {
        Element(automationId).AsTabItem().Select();
        Thread.Sleep(500);
    }

    /// <summary>Text eingeben und das Feld per Tab verlassen; erst das LostFocus speichert.</summary>
    /// <param name="statusId">AutomationId des Statustexts der Seite (ProfileStatusText oder GroupStatusText).</param>
    public async Task SetzeTextAsync(string feldId, string text, Func<SharedConfig, bool> gespeichert, string statusId = "ProfileStatusText")
    {
        Fenster.SetForeground();
        var feld = Element(feldId);
        feld.Focus();
        Thread.Sleep(200);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(text);
        Keyboard.Type(VirtualKeyShort.TAB);
        await WarteAufSpeicherungAsync($"{feldId} = '{text}'", gespeichert, statusId);
    }

    public async Task SetzeHotkeyAsync(HotkeyDefinition hotkey, Func<SharedConfig, bool> gespeichert)
    {
        Fenster.SetForeground();
        var feld = Element("ProfileHotkeyBox");
        feld.Click();
        Thread.Sleep(300);
        DrueckeHotkey(hotkey);
        await WarteAufSpeicherungAsync($"Tastenkürzel {hotkey.Format()} (Feld zeigt '{Wert(feld)}')", gespeichert, "ProfileStatusText");
    }

    /// <summary>Klickt den ersten Listeneintrag, dessen UIA-Name passt.</summary>
    /// <param name="wirkung">
    /// Woran der Klick zu erkennen ist. Ein Klick, der in einen gerade laufenden Speichervorgang fällt, verpufft;
    /// ohne erkennbare Wirkung nach 5 s wird deshalb genau einmal erneut geklickt.
    /// </param>
    public async Task KlickeListenEintragAsync(string listId, Func<string, bool> passt, string beschreibung, Func<bool>? wirkung = null)
    {
        for (var versuch = 1; ; versuch++)
        {
            AutomationElement? eintrag = null;
            // Während eines Speichervorgangs ist die Liste gesperrt, ein Klick ginge ins Leere.
            await WarteBisAsync(() => Element(listId).IsEnabled && (eintrag = FindeListenEintrag(listId, passt)) is not null, TimeSpan.FromSeconds(15),
                () => $"Eintrag '{beschreibung}' fehlt in {listId}. Vorhanden: {string.Join(" | ", ListenNamen(listId))}");

            Fenster.SetForeground();
            eintrag!.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
            Thread.Sleep(200);
            eintrag.Click();
            Thread.Sleep(300);

            if (wirkung is null || versuch == 2)
            {
                return;
            }

            var bis = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < bis)
            {
                if (wirkung())
                {
                    return;
                }

                await Task.Delay(250);
            }
        }
    }

    public bool HatListenEintraege(string listId) => ListenNamen(listId).Count > 0;

    public List<string> ListenNamen(string listId)
    {
        try
        {
            return Element(listId).FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .Select(e => e.Properties.Name.ValueOrDefault ?? string.Empty)
                .ToList();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return new List<string>();
        }
    }

    /// <summary>Profil im Auswahlfeld wählen und warten, bis es reserviert und bearbeitbar ist.</summary>
    public async Task WaehleProfilAsync(string name)
    {
        var combo = Element("ProfileCombo").AsComboBox();
        combo.Expand();
        Thread.Sleep(300);
        try
        {
            // Die Einträge haben keinen eigenen UIA-Namen, deshalb über den angezeigten Text.
            var eintrag = combo.Items.FirstOrDefault(i => Texte(i).Contains(name))
                ?? throw new InvalidOperationException(
                    $"Profil '{name}' nicht im Auswahlfeld. Vorhanden: {string.Join(" | ", combo.Items.SelectMany(Texte))}");
            eintrag.Select();
        }
        finally
        {
            combo.Collapse();
        }

        await WarteAufProfilAsync(name);
    }

    /// <summary>Legt über "+" ein Profil an und wartet, bis es gewählt und bearbeitbar ist.</summary>
    /// <returns>Id des neuen Profils.</returns>
    public async Task<Guid> NeuesProfilAsync()
    {
        var vorher = KonfigVorher().AlarmProfiles.Select(p => p.Id).ToHashSet();
        Element("NewProfileButton").AsButton().Invoke();
        Guid? neu = null;
        await WarteBisAsync(() => (neu = ProfilIds().Except(vorher).Cast<Guid?>().FirstOrDefault()) is not null, SpeicherTimeout,
            "Neues Alarm-Profil ist nicht in shared-config.json angekommen.");
        await WarteAufProfilAsync("Neues Profil");
        return neu!.Value;
    }

    /// <summary>Legt über "Neue Gruppe" eine Gruppe an und wartet, bis sie gewählt und bearbeitbar ist.</summary>
    /// <returns>Id der neuen Gruppe.</returns>
    public async Task<Guid> NeueGruppeAsync()
    {
        // "Alle" steht erst nach dem ersten Speichern in der Datei und wäre sonst die "neue" Gruppe.
        static HashSet<Guid> GruppenIds(SharedConfig? config) =>
            (config?.DeviceGroups ?? new()).Where(g => !g.IsBuiltInAllDevicesGroup).Select(g => g.Id).ToHashSet();

        var vorher = GruppenIds(KonfigVorher());
        Element("NewGroupButton").AsButton().Invoke();
        Guid? neu = null;
        await WarteBisAsync(() => (neu = GruppenIds(ZweiGeraete.LiesConfig()).Except(vorher).Cast<Guid?>().FirstOrDefault()) is not null, SpeicherTimeout,
            "Neue Gruppe ist nicht in shared-config.json angekommen.");
        await WarteBisAsync(() => Wert(Element("GroupNameBox")) == "Neue Gruppe" && Element("GroupNameBox").IsEnabled, LockTimeout,
            () => $"Neue Gruppe wird nicht bearbeitbar: Edit-Lock nicht gewährt (Statustext: '{Statustext("GroupStatusText")}').");
        return neu!.Value;
    }

    /// <summary>Wartet, bis das Profil im Formular steht und die Reservierung (Edit-Lock) gewährt ist.</summary>
    public Task WarteAufProfilAsync(string name) =>
        WarteBisAsync(() => Wert(Element("ProfileNameBox")) == name && Element("ProfileNameBox").IsEnabled, LockTimeout,
            () => $"Profil '{name}' wird nicht bearbeitbar (Formular zeigt '{Wert(Element("ProfileNameBox"))}', " +
            $"Statustext '{Statustext()}'): Edit-Lock nicht gewährt?");

    public string Statustext(string statusId = "ProfileStatusText")
    {
        try
        {
            return Element(statusId).Name;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private async Task WarteAufSpeicherungAsync(string was, Func<SharedConfig, bool> gespeichert, string statusId)
    {
        await WarteBisAsync(() => ZweiGeraete.LiesConfig() is { } c && gespeichert(c), SpeicherTimeout,
            () => $"{was} ist nach {SpeicherTimeout.TotalSeconds:0} s nicht in shared-config.json (Statustext: '{Statustext(statusId)}').");
        await WarteBisAsync(() => Statustext(statusId) == Gespeichert, SpeicherTimeout,
            () => $"{was}: Statustext ist '{Statustext(statusId)}' statt '{Gespeichert}'.");
    }

    private AutomationElement? FindeListenEintrag(string listId, Func<string, bool> passt)
    {
        try
        {
            return Element(listId).FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .FirstOrDefault(e => passt(e.Properties.Name.ValueOrDefault ?? string.Empty));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static HashSet<Guid> ProfilIds() => (ZweiGeraete.LiesConfig()?.AlarmProfiles ?? new()).Select(p => p.Id).ToHashSet();

    // Ein gerade nicht lesbarer Vorher-Stand ließe jedes vorhandene Profil als "neu" erscheinen.
    private static SharedConfig KonfigVorher()
    {
        for (var i = 0; i < 20; i++)
        {
            if (!File.Exists(AppPaths.SharedConfigFilePath))
            {
                return new SharedConfig();
            }

            if (ZweiGeraete.LiesConfig() is { } config)
            {
                return config;
            }

            Thread.Sleep(250);
        }

        throw new IOException($"{AppPaths.SharedConfigFilePath} ist nicht lesbar.");
    }

    private static List<string> Texte(AutomationElement eintrag) =>
        eintrag.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Select(t => t.Properties.Name.ValueOrDefault ?? string.Empty)
            .Prepend(eintrag.Properties.Name.ValueOrDefault ?? string.Empty)
            .ToList();

    private static string Wert(AutomationElement feld) =>
        feld.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault ?? feld.Properties.Name.ValueOrDefault ?? string.Empty;
}
