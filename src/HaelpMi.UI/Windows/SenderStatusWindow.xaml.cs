using System.Windows;
using HaelpMi.Core.Sending;

namespace HaelpMi.UI.Windows;

/// <summary>
/// Sender-side hover info popup (FR-53, extends Phase 1's plain receipt banner): shows
/// live status ("gesendet" / "abgebrochen" / "beendet"), "Empfangen: x von y", the
/// "Auf dem Weg" name list, and a manual Abbrechen button while a
/// <see cref="RepeatingAlarmSession"/> is still actively repeating. Stays open after the
/// session ends until manually dismissed (Phase 1 FR-14 behavior, unchanged) - only the
/// Abbrechen button's availability is tied to whether the session is still running.
/// </summary>
public partial class SenderStatusWindow : Window
{
    private static readonly List<SenderStatusWindow> OpenWindows = new();

    private readonly RepeatingAlarmSession _session;

    public SenderStatusWindow(RepeatingAlarmSession session, string profileName)
    {
        InitializeComponent();
        _session = session;
        ProfileText.Text = profileName;

        _session.StatusChanged += Session_StatusChanged;
        _session.Finished += Session_Finished;

        ApplyStatus(new AlarmSessionStatus
        {
            TargetCount = session.Targets.Count,
            AckedCount = 0,
            OnTheWayNames = Array.Empty<string>(),
            StillSending = true,
        });

        Loaded += (_, _) => PositionInCorner();
        Closed += (_, _) =>
        {
            OpenWindows.Remove(this);
            _session.StatusChanged -= Session_StatusChanged;
            _session.Finished -= Session_Finished;
        };
        OpenWindows.Add(this);
    }

    private void Session_StatusChanged(object? sender, AlarmSessionStatus status) =>
        Dispatcher.BeginInvoke(() => ApplyStatus(status));

    private void Session_Finished(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            CancelButton.IsEnabled = false;
            StatusText.Text = "Alarm beendet";
        });

    private void ApplyStatus(AlarmSessionStatus status)
    {
        StatusText.Text = status.StillSending ? "Alarm wird gesendet..." : "Alarm beendet";
        AckedText.Text = $"Empfangen: {status.AckedCount} von {status.TargetCount}";
        CancelButton.IsEnabled = status.StillSending;

        OnTheWayList.ItemsSource = status.OnTheWayNames;
        OnTheWayHeaderText.Visibility = status.OnTheWayNames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PositionInCorner()
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 16;
        var stackIndex = OpenWindows.IndexOf(this);
        var verticalOffset = stackIndex * (ActualHeight + 10);

        Left = workArea.Right - Width - margin;
        Top = workArea.Bottom - ActualHeight - margin - verticalOffset;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _session.Cancel();

    private void CloseBannerButton_Click(object sender, RoutedEventArgs e) => Close();
}
