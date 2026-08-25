using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace HaelpMi.UI.Helpers;

/// <summary>
/// Ersatz für die native Hover-Markierung (ComboBoxItem.IsHighlighted), die in manchen VM/RDP-
/// Sitzungen ausbleibt - dort werden WM_MOUSEMOVE-Nachrichten zur Bandbreitenreduktion
/// vermutlich gedrosselt/koalesziert, wovon die dafür zuständigen MouseEnter/Leave-Events der
/// ComboBoxItems abhängen. Statt auf zugestellte Move-Events zu warten, fragt ein Timer die
/// tatsächliche Cursorposition aktiv ab - bewusst per Win32 GetCursorPos statt
/// System.Windows.Input.Mouse.GetPosition(), das nur WPFs zuletzt aus einem Move-Event
/// zwischengespeicherte Position zurückgibt und damit vom selben Ausbleiben betroffen wäre,
/// das hier gerade umgangen werden soll - und markiert das darunterliegende Element selbst.
/// Aktivierung per XAML: Enabled="True" auf dem Popup, IsCursorOver als zusätzlicher Trigger
/// am Item-Style.
/// </summary>
public static class HoverTrackingHelper
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(HoverTrackingHelper), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(Popup popup, bool value) => popup.SetValue(EnabledProperty, value);
    public static bool GetEnabled(Popup popup) => (bool)popup.GetValue(EnabledProperty);

    public static readonly DependencyProperty IsCursorOverProperty = DependencyProperty.RegisterAttached(
        "IsCursorOver", typeof(bool), typeof(HoverTrackingHelper), new PropertyMetadata(false));

    public static bool GetIsCursorOver(UIElement element) => (bool)element.GetValue(IsCursorOverProperty);

    private static readonly DependencyProperty TrackerProperty = DependencyProperty.RegisterAttached(
        "Tracker", typeof(Tracker), typeof(HoverTrackingHelper));

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Popup popup || e.NewValue is not true)
        {
            return;
        }

        popup.Opened += (_, _) => new Tracker(popup).Start();
        popup.Closed += (_, _) => (popup.GetValue(TrackerProperty) as Tracker)?.Stop();
    }

    // Eigene Klasse statt loser Felder, weil pro Popup-Öffnung ein eigener "zuletzt
    // markiertes Element"-Zustand gebraucht wird, den Stop() beim Schließen sauber
    // zurücksetzen muss - eine Closure allein hätte keinen Weg, sich selbst zu stoppen.
    private sealed class Tracker(Popup popup)
    {
        private readonly DispatcherTimer _timer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(80) };
        private UIElement? _current;

        public void Start()
        {
            popup.SetValue(TrackerProperty, this);
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            SetCurrent(null);
            popup.ClearValue(TrackerProperty);
        }

        private void Tick()
        {
            if (popup.Child is not UIElement root || !GetCursorPos(out var screenPoint))
            {
                return;
            }

            var localPoint = root.PointFromScreen(new Point(screenPoint.X, screenPoint.Y));
            var hit = root.InputHitTest(localPoint) as DependencyObject;
            SetCurrent(FindComboBoxItem(hit));
        }

        private void SetCurrent(UIElement? element)
        {
            if (ReferenceEquals(element, _current))
            {
                return;
            }

            _current?.SetValue(IsCursorOverProperty, false);
            element?.SetValue(IsCursorOverProperty, true);
            _current = element;
        }

        private static UIElement? FindComboBoxItem(DependencyObject? source)
        {
            for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is ComboBoxItem item)
                {
                    return item;
                }
            }
            return null;
        }
    }
}
