using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace HaelpMi.UI.Helpers;

/// <summary>
/// Zwei Absicherungen gegen einen unter RDP/VM gemeldeten Bug, bei dem der erste Klick auf ein
/// Popup-Element (z. B. ein ComboBoxItem) wirkungslos bleibt - Auswahl bleibt auf dem vorherigen
/// Eintrag stehen: WS_EX_NOACTIVATE verhindert, dass das Popup-Fenster beim Öffnen als eigene
/// Fensteraktivierung behandelt wird (die sonst den ersten Klick statt an das Element an die
/// Aktivierung selbst gehen lässt); Mouse.Capture bindet die Maus zusätzlich explizit an den
/// Popup-Inhalt, statt sich auf die implizite Windows-Zuordnung zu verlassen. Aktivierung per
/// XAML: Enabled="True" auf dem Popup.
/// </summary>
public static class PopupNoActivateHelper
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(PopupNoActivateHelper), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(Popup popup, bool value) => popup.SetValue(EnabledProperty, value);
    public static bool GetEnabled(Popup popup) => (bool)popup.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Popup popup && e.NewValue is true)
        {
            popup.Opened += (_, _) => OnOpened(popup);
            popup.Closed += (_, _) => OnClosed(popup);
        }
    }

    private static void OnOpened(Popup popup)
    {
        if (PresentationSource.FromVisual(popup.Child) is HwndSource hwndSource)
        {
            var exStyle = GetWindowLong(hwndSource.Handle, GwlExStyle);
            SetWindowLong(hwndSource.Handle, GwlExStyle, exStyle | WsExNoActivate);
        }

        Mouse.Capture(popup.Child, CaptureMode.SubTree);
    }

    private static void OnClosed(Popup popup)
    {
        if (Mouse.Captured == popup.Child)
        {
            Mouse.Capture(null);
        }
    }

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
