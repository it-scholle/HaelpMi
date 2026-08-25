using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace HaelpMi.UI.Helpers;

/// <summary>
/// Verhindert per WS_EX_NOACTIVATE, dass ein Popup beim Öffnen zum aktiven Fenster wird. Ohne
/// das behandelt Windows den ersten Klick auf ein Popup-Element (z. B. ein ComboBoxItem) unter
/// RDP/VM-Sitzungen als reine Fensteraktivierung und reicht ihn nicht an das Element weiter -
/// die Auswahl ändert sich dadurch nicht. Aktivierung per XAML: Enabled="True" auf dem Popup.
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
            popup.Opened += (_, _) => ApplyNoActivate(popup);
        }
    }

    private static void ApplyNoActivate(Popup popup)
    {
        if (PresentationSource.FromVisual(popup.Child) is HwndSource hwndSource)
        {
            var exStyle = GetWindowLong(hwndSource.Handle, GwlExStyle);
            SetWindowLong(hwndSource.Handle, GwlExStyle, exStyle | WsExNoActivate);
        }
    }

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
