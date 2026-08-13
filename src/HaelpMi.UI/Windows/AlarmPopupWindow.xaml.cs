using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using HaelpMi.Core.Interop;
using HaelpMi.Core.Models;

namespace HaelpMi.UI.Windows;

/// <summary>
/// Forced-foreground alarm popup (FR-9/FR-10), reworked for Teil 2: room+number
/// prominent, username small with RDP marker (FR-40/FR-41), and no longer a single
/// "acknowledge" action - closable only once the profile's response threshold has been
/// reached (FR-47/FR-51), with a "bin unterwegs" action that reports back to the sender
/// and an unconditional auto-close 1 minute after the *last* received signal (FR-52).
///
/// One instance corresponds to one <see cref="AlarmSessionId"/>. The hosting coordinator
/// (HaelpMi.Agent) is responsible for the "reopens on the next received signal" part of
/// FR-51: it keeps at most one live window per session and only calls
/// <see cref="NotifyNewSignalReceived"/> on repeats while this window is still alive -
/// if the window was closed (by any means) before the sender's repeat loop finishes, the
/// next repeat simply constructs a brand new instance, since there is no window left to
/// notify. That single rule is what makes "reopen if closed early" fall out naturally
/// without needing to separately detect *why* a previous instance closed.
/// </summary>
public partial class AlarmPopupWindow : Window
{
    private static int _openCount;

    private readonly IDisposable _screensaverGuard;
    private readonly int _responseThreshold;
    private readonly DispatcherTimer _autoCloseTimer;
    private DateTimeOffset _lastSignalUtc;
    private bool _thresholdReached;
    private bool _autoClosing;

    public Guid AlarmProfileId { get; }
    public Guid AlarmSessionId { get; }

    /// <summary>Raised when "Bin unterwegs" is clicked - the hosting coordinator sends the actual network message (FR-51).</summary>
    public event EventHandler? OnMyWayRequested;

    public AlarmPopupWindow(
        string senderComputerName,
        string senderUser,
        string senderRoomName,
        string senderRoomNumber,
        bool senderIsRemoteSession,
        string messageText,
        int responseThreshold,
        Guid alarmProfileId,
        Guid alarmSessionId,
        DateTimeOffset sentAtUtc,
        bool isTest = false)
    {
        InitializeComponent();

        AlarmProfileId = alarmProfileId;
        AlarmSessionId = alarmSessionId;
        _responseThreshold = Math.Max(1, responseThreshold);
        _lastSignalUtc = sentAtUtc;

        RoomText.Text = string.IsNullOrWhiteSpace(senderRoomNumber) ? senderRoomName : $"{senderRoomName} ({senderRoomNumber})";
        MessageText.Text = messageText;
        // FR-41: RDP-Kennzeichnung ist eine reine Live-Sitzungs-Eigenschaft (siehe DeviceEntry.IsRemoteSession) - keine Historie.
        var remoteSuffix = senderIsRemoteSession ? " - (Remote)" : string.Empty;
        SenderSubText.Text = $"Ausgelöst von: {senderComputerName} - {senderUser}{remoteSuffix}";
        TimestampText.Text = sentAtUtc.ToLocalTime().ToString("HH:mm:ss");
        UpdateThresholdStatus(0);

        // Testmodus-Toggle (Nutzerwunsch 13.08.2026): Grün statt Rot + Wasserzeichen, damit ein
        // Empfänger einen Testalarm nie mit einem echten Notruf verwechselt (siehe TestModeArmState-
        // Klassendoku für die Sicherheitsbegründung auf Sender-Seite). Datenschutz-Layout (Raum
        // prominent, Username klein) bleibt unangetastet - nur Rahmenfarbe/Kopfzeile/Wasserzeichen ändern sich.
        if (isTest)
        {
            var successBrush = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            RootBorder.BorderBrush = successBrush;
            HeaderBorder.Background = successBrush;
            HeaderSubText.Text = "HälpMi - TESTALARM";
            TestWatermarkText.Visibility = Visibility.Visible;
        }

        var offset = (System.Threading.Interlocked.Increment(ref _openCount) - 1) % 6 * 28;
        Loaded += (_, _) =>
        {
            Left += offset;
            Top += offset;
        };

        _screensaverGuard = ScreensaverGuard.SuppressWhileAlarmShown();

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoCloseTimer.Tick += AutoCloseTimer_Tick;
        _autoCloseTimer.Start();

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            System.Threading.Interlocked.Decrement(ref _openCount);
            _autoCloseTimer.Stop();
            _screensaverGuard.Dispose();
        };
    }

    /// <summary>Call for every repeat of the same session while this window is still open (FR-52: resets the 1-minute-since-last-signal auto-close).</summary>
    public void NotifyNewSignalReceived(DateTimeOffset sentAtUtc)
    {
        _lastSignalUtc = sentAtUtc;
        TimestampText.Text = sentAtUtc.ToLocalTime().ToString("HH:mm:ss");

        // A repeat means the sender is still actively alarming - bring the window back
        // to the foreground in case the user switched away from it in the meantime.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            ForegroundHelper.ForceForeground(hwnd);
        }
    }

    /// <summary>Call whenever a new <see cref="Networking.Protocol.AlarmStatusRelayMessage"/> arrives for this session (FR-47/FR-51).</summary>
    public void UpdateOnTheWayCount(int onTheWayCount)
    {
        _thresholdReached = onTheWayCount >= _responseThreshold;
        CloseButton.IsEnabled = _thresholdReached;
        UpdateThresholdStatus(onTheWayCount);
    }

    private void UpdateThresholdStatus(int onTheWayCount)
    {
        var remaining = Math.Max(0, _responseThreshold - onTheWayCount);
        ThresholdStatusText.Text = remaining == 0
            ? $"{onTheWayCount} Person(en) sind unterwegs - Fenster kann jetzt geschlossen werden."
            : $"{onTheWayCount} Person(en) sind unterwegs - noch {remaining} bis das Fenster schließbar wird.";
    }

    private void AutoCloseTimer_Tick(object? sender, EventArgs e)
    {
        if (DateTimeOffset.UtcNow - _lastSignalUtc < AppConstants.AlarmAutoCloseAfterLastSignal)
        {
            return;
        }

        _autoCloseTimer.Stop();
        _autoClosing = true; // FR-52: unconditional - bypasses the threshold gate below
        Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ForegroundHelper.ForceForeground(hwnd);

        // A second attempt shortly after helps against some fullscreen apps that
        // re-assert their own foreground state right after losing it (5.3/6.).
        var retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        retryTimer.Tick += (_, _) =>
        {
            retryTimer.Stop();
            if (IsVisible)
            {
                ForegroundHelper.ForceForeground(hwnd);
            }
        };
        retryTimer.Start();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_thresholdReached && !_autoClosing)
        {
            e.Cancel = true; // FR-51: "nicht über normale Fenstersteuerung deaktivierbar", erst ab Schwellwert oder Auto-Close
        }
    }

    private void OnMyWayButton_Click(object sender, RoutedEventArgs e)
    {
        OnMyWayButton.IsEnabled = false;
        OnMyWayRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
