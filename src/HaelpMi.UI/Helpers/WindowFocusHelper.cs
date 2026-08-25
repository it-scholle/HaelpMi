using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HaelpMi.UI.Helpers;

/// <summary>
/// Für den verbreiteten Window_PreviewMouseDown-Handler (erzwingt Keyboard.Focus(this) beim
/// Klick irgendwo im Fenster, damit ein zuvor fokussiertes Textfeld per Blur speichert):
/// Klicks, die innerhalb einer ComboBox landen, müssen ausgenommen werden - sonst reißt der
/// Fokuswechsel auf das Fenster der ComboBox mitten im Öffnen ihres Popups den Fokus weg,
/// wodurch der anschließende Klick auf ein Popup-Element nicht mehr als Auswahl zählt.
/// </summary>
public static class WindowFocusHelper
{
    public static bool IsWithinComboBox(DependencyObject source)
    {
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (current is ComboBox or ComboBoxItem)
            {
                return true;
            }
        }
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject d) =>
        (d is Visual ? VisualTreeHelper.GetParent(d) : null) ?? LogicalTreeHelper.GetParent(d);
}
