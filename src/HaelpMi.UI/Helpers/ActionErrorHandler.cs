using System.Windows;
using HaelpMi.Core.Diagnostics;

namespace HaelpMi.UI.Helpers;

/// <summary>
/// Gemeinsamer Fallback für Button-/Aktions-Handler in allen Dashboard-/Konfigurations-
/// Fenstern: ein async-void-Handler ohne eigenes try/catch lässt eine Ausnahme nur bis
/// zum globalen DispatcherUnhandledException-Handler durchlaufen - der loggt zwar (siehe
/// CrashLogger), zeigt aber nichts im Fenster. Für den Nutzer sieht das dann aus wie
/// "der Button tut einfach nichts" (zwei konkrete Vorfälle rund ums Admin-Dashboard am
/// 04.08.2026 - dort ging es um OnStartup, hier geht es um dieselbe Lücke bei jedem
/// einzelnen Aktions-Button). Aufrufer fangen gezielt und rufen dies statt dessen auf.
/// </summary>
public static class ActionErrorHandler
{
    public static void Show(object caller, string action, Exception ex)
    {
        CrashLogger.Log(nameof(HaelpMi.UI), $"{caller.GetType().Name}.{action}", ex);
        MessageBox.Show($"{action} ist fehlgeschlagen:{Environment.NewLine}{ex.Message}",
            "HälpMi - Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
