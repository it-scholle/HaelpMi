using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using HaelpMi.Core.Models;

namespace HaelpMi.E2E.Tests;

/// <summary>Findet und bedient die installierte HälpMi-App (Agent + Konfigurationsprogramm) über UI Automation.</summary>
internal sealed class HaelpMiApp : IDisposable
{
    public const string AgentProzess = "HaelpMi.Agent";
    public const string KonfigProzess = "HaelpMi.Config";

    public const string TitelEinstellungen = "HälpMi - Einstellungen";
    public const string TitelDashboard = "HälpMi - Admin-Dashboard";
    public const string AlarmfensterId = "AlarmPopupWindow";

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

    public static async Task WarteAufAgentAsync(TimeSpan timeout)
    {
        var frist = DateTime.UtcNow + timeout;
        while (!Prozess.Laeuft(AgentProzess))
        {
            if (DateTime.UtcNow > frist)
            {
                throw new TimeoutException($"{AgentProzess} läuft nach {timeout} nicht.");
            }

            await Task.Delay(500);
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

    public Window WarteAufAlarmfenster(TimeSpan timeout) => WarteAufFenster(AgentProzess, null, AlarmfensterId, timeout);

    public bool AlarmfensterOffen() => FindeFenster(AgentProzess, null, AlarmfensterId) is not null;

    /// <param name="titel">Fenstertitel (UIA-Name) oder null.</param>
    /// <param name="automationId">AutomationId des Fensters oder null.</param>
    public Window WarteAufFenster(string prozessName, string? titel, string? automationId, TimeSpan timeout)
    {
        var frist = DateTime.UtcNow + timeout;
        while (true)
        {
            if (FindeFenster(prozessName, titel, automationId) is { } fenster)
            {
                return fenster;
            }

            if (DateTime.UtcNow > frist)
            {
                throw new TimeoutException($"Fenster '{titel ?? automationId}' von {prozessName} ist nach {timeout} nicht erschienen.");
            }

            Thread.Sleep(300);
        }
    }

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

    public void Dispose() => Automation.Dispose();

    private Window? FindeFenster(string prozessName, string? titel, string? automationId)
    {
        var pids = Process.GetProcessesByName(prozessName).Select(p => { using (p) return p.Id; }).ToHashSet();
        if (pids.Count == 0)
        {
            return null;
        }

        foreach (var element in Automation.GetDesktop().FindAllChildren())
        {
            try
            {
                if (!pids.Contains(element.Properties.ProcessId.ValueOrDefault))
                {
                    continue;
                }

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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
