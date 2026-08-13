using System.Windows;
using HaelpMi.Core.Models;

namespace HaelpMi.UI.ViewModels;

/// <summary>
/// Everything ConfigWindow needs from the outside world, injected by the hosting app
/// (HaelpMi.Config or HaelpMi.Agent's self-test path) so this UI project stays free of
/// IPC/transport specifics - it only knows "load settings", "save devices", etc.
/// </summary>
public sealed class ConfigWindowContext
{
    public required Func<OwnSettings> LoadSettings { get; init; }
    public required Action<OwnSettings> SaveSettings { get; init; }
    public required Func<List<DeviceEntry>> LoadDevices { get; init; }
    public required Action<List<DeviceEntry>> SaveDevices { get; init; }

    /// <summary>
    /// Bugfix 08.08.2026: LoadDevices enthält nur über Boot-Call entdeckte PEERS, nie das
    /// eigene Gerät (dasselbe Prinzip wie AdminDashboardContext.LoadOwnDevice) - ohne das
    /// hier ebenfalls verfügbar zu haben, verschwindet ein Alarm-Profil aus "Meine Alarme",
    /// sobald das eigene Gerät (direkt/über Gruppe/über Raum) zu seinen eigenen Empfängern
    /// zählt, siehe RebuildMyAlarms().
    /// </summary>
    public required Func<DeviceEntry> LoadOwnDevice { get; init; }

    /// <summary>
    /// Nutzerwunsch 05.08.2026: "Meine Alarme" - eine reine Lesansicht, welche Alarm-Profile
    /// dieses Gerät (als Sender, direkt/über Raum/über Gruppe) überhaupt betreffen, mit
    /// Tastenkürzel und den für dieses Gerät konfigurierten Empfängern. Der eigentliche
    /// User bekommt sonst nirgends zu sehen, was sein eigener Hotkey auslöst.
    /// </summary>
    public required Func<SharedConfig> LoadConfig { get; init; }

    /// <summary>Tell the always-running Agent that "Allgemein" changed, so it re-broadcasts identity and re-registers the hotkey (FR-17).</summary>
    public required Func<Task<bool>> RequestRebroadcast { get; init; }

    /// <summary>"Erneut suchen" (FR-20).</summary>
    public required Func<Task<bool>> RequestSearchAgain { get; init; }

    /// <summary>
    /// "Testalarm an mich selbst senden" (FR-27). The Agent performs the full loopback
    /// send/receive itself and shows its own popup + receipt banner - this only reports
    /// whether the Agent accepted the request, so ConfigWindow doesn't duplicate that UI.
    /// </summary>
    public required Func<Task<bool>> RequestSelfTest { get; init; }

    /// <summary>Testmodus-Toggle scharfschalten (Nutzerwunsch 13.08.2026): One-Shot für den nächsten Hotkey-Alarm, siehe TestModeArmState-Klassendoku.</summary>
    public required Func<Task<bool>> RequestArmTestMode { get; init; }

    /// <summary>Manuelles Wieder-Ausschalten - reiner UX-Komfort, keine Sicherheitsfunktion (der 2-Minuten-Timeout gilt unabhängig davon).</summary>
    public required Func<Task<bool>> RequestDisarmTestMode { get; init; }

    /// <summary>Aktuellen Testmodus-Countdown abfragen (Re-Sync beim Öffnen/Fokussieren) - null, falls gerade nicht scharf.</summary>
    public required Func<Task<TimeSpan?>> RequestTestModeStatus { get; init; }

    /// <summary>
    /// Opens the Admin-Dashboard (FR-33: nur für Role.Admin relevant). Null on a User-role
    /// device - ConfigWindow hides the button entirely rather than showing a disabled one.
    /// Async, weil zuerst der exklusive Edit-Lock (CLAUDE.md, pro Kreis) über das Netzwerk
    /// eingeholt wird - die Methode zeigt bei Ablehnung selbst eine Fehlermeldung und lässt
    /// das Fenster dann einfach zu (kein Rückgabewert nötig).
    /// </summary>
    public Func<Task>? OpenAdminDashboard { get; init; }
}
