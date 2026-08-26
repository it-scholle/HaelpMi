using System.Collections.Concurrent;
using HaelpMi.Core.Audio;
using HaelpMi.Core.Interop;
using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Sending;
using HaelpMi.Core.Storage;
using HaelpMi.UI.Windows;

namespace HaelpMi.Agent;

/// <summary>
/// Glues the Core networking/audio pieces to the shared WPF windows (Teil 2, Abschnitt
/// 7/8). Receiver side: an incoming <see cref="AlarmRequestMessage"/> becomes "show/refresh
/// a threshold-gated popup + play a sound" (FR-47/FR-51/FR-52); sender side: a triggered
/// <see cref="AlarmProfile"/> becomes "resolve recipients + run a repeating session + show
/// a status popup" (FR-50/FR-53).
///
/// One <see cref="AlarmPopupWindow"/> per <see cref="AlarmRequestMessage.AlarmSessionId"/>
/// is the whole trick behind FR-51's "öffnet sich beim nächsten Signal erneut, falls
/// vorzeitig geschlossen": a repeat for a session with no tracked window simply creates a
/// fresh one, since the previous one (closed for whatever reason) can no longer be found.
/// </summary>
public sealed class AlarmFlowCoordinator
{
    private readonly Func<LiveIdentity> _identityProvider;
    private readonly Func<OwnSettings> _settingsProvider;
    private readonly Func<SharedConfig> _sharedConfigProvider;
    private readonly AlarmFeedbackChannel _feedbackChannel;
    private readonly AuditLog _auditLog = new();
    private readonly AlarmSender _sender;
    private readonly DeviceStore _deviceStore = new();
    private readonly MultiDeviceAlarmPlayer _audioPlayer;
    private readonly ConcurrentDictionary<Guid, AlarmPopupWindow> _openPopups = new();

    public AlarmFlowCoordinator(
        Func<LiveIdentity> identityProvider,
        Func<OwnSettings> settingsProvider,
        Func<SharedConfig> sharedConfigProvider,
        AlarmFeedbackChannel feedbackChannel)
    {
        _identityProvider = identityProvider;
        _settingsProvider = settingsProvider;
        _sharedConfigProvider = sharedConfigProvider;
        _feedbackChannel = feedbackChannel;
        _sender = new AlarmSender(_auditLog.Append);
        _audioPlayer = new MultiDeviceAlarmPlayer(_auditLog.Append);
        _feedbackChannel.StatusRelayReceived += (_, relay) => HandleStatusRelay(relay);
    }

    /// <summary>Called from the TCP listener's background thread when an alarm arrives (FR-9/FR-47).</summary>
    public void HandleIncomingAlarmRequest(AlarmReceivedEventArgs args)
    {
        var request = args.Request;

        // Bugfix 25.08.2026 (Issue #9-Nachtrag): bei Fast User Switching bekommt JEDE
        // angemeldete Sitzung ein empfangenes Alarmsignal (siehe AlarmChannel), auch eine
        // gerade weggeschaltete. Ein NEUES erzwungenes Popup dort kann niemand sehen oder
        // wegklicken - lieber gar nicht erst zeigen, als eine Zustellung vortäuschen, die
        // niemand wahrnimmt. Der Ack ans sendende Gerät ist davon unabhängig (Geräte-Ebene,
        // siehe AlarmTcpListener) - nur die lokale Anzeige wird hier unterdrückt. Ein schon
        // offenes Popup (aus der Zeit, als diese Sitzung noch aktiv war) wird trotzdem
        // weiter aktualisiert, siehe unten - das erzeugt kein neues Popup.
        if (!_openPopups.ContainsKey(request.AlarmSessionId) && !ActiveSessionDetector.IsCurrentSessionActive())
        {
            _auditLog.Append($"alarm popup unterdrückt (Sitzung nicht aktiv sichtbar): alarmSessionId={request.AlarmSessionId}");
            return;
        }

        var settings = _settingsProvider();
        var soundOption = IncomingSoundCatalog.Resolve(settings.IncomingSoundId);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_openPopups.TryGetValue(request.AlarmSessionId, out var existing))
            {
                // Same alarm, still on screen - just refresh it (FR-52), don't spam a
                // second window for every 5-second repeat.
                existing.NotifyNewSignalReceived(request.SentAtUtc);
                return;
            }

            var popup = new AlarmPopupWindow(
                request.SenderComputerName,
                request.SenderUser,
                request.SenderRoomName,
                request.SenderRoomNumber,
                request.SenderIsRemoteSession,
                request.Text,
                request.ResponseThreshold,
                request.AlarmProfileId,
                request.AlarmSessionId,
                request.SentAtUtc);

            popup.OnMyWayRequested += (_, _) => _ = ReportOnMyWayAsync(request, args);
            popup.Closed += (_, _) => _openPopups.TryRemove(request.AlarmSessionId, out _);

            _openPopups[request.AlarmSessionId] = popup;
            popup.Show();
        });

        _ = _audioPlayer.PlayOnAllActiveDevicesAsync(soundOption);
    }

    /// <summary>Every aggregated status update from the sender (FR-51): keeps a still-open receiver popup's threshold gate current.</summary>
    private void HandleStatusRelay(AlarmStatusRelayMessage relay)
    {
        if (!_openPopups.TryGetValue(relay.AlarmSessionId, out var popup))
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() => popup.UpdateOnTheWayCount(relay.OnTheWayUserNames.Count));
    }

    private async Task ReportOnMyWayAsync(AlarmRequestMessage request, AlarmReceivedEventArgs args)
    {
        var identity = _identityProvider();
        var senderTarget = new DeviceEntry
        {
            DeviceId = request.SenderDeviceId,
            IpAddress = args.SenderAddress.ToString(),
        };

        var message = new AlarmOnMyWayMessage(
            identity.CustomerGroupId, request.AlarmProfileId, request.AlarmSessionId,
            identity.DeviceId, identity.ComputerName, identity.User, identity.RoomName, DateTimeOffset.UtcNow);

        await _feedbackChannel.SendOnMyWayAsync(senderTarget, message);
    }

    /// <summary>Hotkey-triggered send for one <see cref="AlarmProfile"/> (FR-50): resolves this sender's asymmetric recipient set and starts a repeating session.</summary>
    public void TriggerAlarmProfile(AlarmProfile profile)
    {
        var identity = _identityProvider();
        var devices = _deviceStore.Load();
        var groups = _sharedConfigProvider().DeviceGroups;
        var targets = RecipientResolver.ResolveRecipientsForSender(profile, identity.DeviceId, identity.RoomNumber, devices, groups, excludeSender: true);
        if (targets.Count == 0)
        {
            return; // nothing to send, nothing to show (Teil 2, Abschnitt 4: an empty recipient set is a valid, if useless, admin configuration)
        }

        var session = new RepeatingAlarmSession(profile, targets, identity, _sender, _feedbackChannel, DateTimeOffset.UtcNow);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new SenderStatusWindow(session, profile.Name);
            window.Show();
        });

        _ = RunSessionAsync(session);
    }

    private static async Task RunSessionAsync(RepeatingAlarmSession session)
    {
        try
        {
            await session.RunAsync();
        }
        finally
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// "Testalarm an mich selbst senden" (FR-27). Läuft seit Nutzerwunsch 20.08.2026 durch
    /// exakt dieselbe Pipeline wie ein echter Alarm (<see cref="RepeatingAlarmSession"/> +
    /// <see cref="SenderStatusWindow"/> mit "gesendet"-Banner und funktionierendem
    /// Abbrechen-Button), nur mit sich selbst als einzigem Ziel - vorher war das ein
    /// isolierter Einzel-Versand ohne jede Rückmeldeschleife: kein Sender-seitiger Listener
    /// nahm die eigene "Ich komme"-Antwort entgegen und relayte sie zurück, wodurch das
    /// Empfänger-Popup nach Klick auf "Ich komme" nie schließbar wurde (Fehlerbericht
    /// "Selbsttest lässt sich nach Ich komme nicht schließen") und auch kein Sender-Banner
    /// erschien. Der Schwellwert wird dabei IMMER auf 1 erzwungen, unabhängig vom im Profil
    /// hinterlegten Wert - bei einem Selbsttest kann ohnehin nur man selbst antworten, ein
    /// höherer, für echte Alarme gedachter Schwellwert wäre hier nie erreichbar.
    /// </summary>
    public Task<bool> SendSelfTestAsync(AlarmProfile profile)
    {
        var identity = _identityProvider();
        var selfTarget = new DeviceEntry
        {
            DeviceId = identity.DeviceId,
            ComputerName = identity.ComputerName,
            User = identity.User,
            RoomName = identity.RoomName,
            RoomNumber = identity.RoomNumber,
            IpAddress = "127.0.0.1",
            TcpPort = AppConstants.AlarmTcpPort,
        };

        var selfTestProfile = new AlarmProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Text = profile.Text,
            Hotkey = profile.Hotkey,
            ResponseThreshold = 1,
            RecipientAssignments = profile.RecipientAssignments,
        };

        var session = new RepeatingAlarmSession(selfTestProfile, new[] { selfTarget }, identity, _sender, _feedbackChannel, DateTimeOffset.UtcNow);

        // Der Rückgabewert dieser Methode war schon immer nur "kam der allererste Versand
        // an" (IPC-Antwort ans ConfigWindow, siehe HandleSelfTestRequestAsync) - das bleibt
        // unverändert, auch wenn die Session danach im Hintergrund weiterläuft.
        var firstStatus = new TaskCompletionSource<bool>();
        void OnFirstStatus(object? _, AlarmSessionStatus status)
        {
            session.StatusChanged -= OnFirstStatus;
            firstStatus.TrySetResult(status.AckedCount > 0);
        }
        session.StatusChanged += OnFirstStatus;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new SenderStatusWindow(session, $"{profile.Name} (Selbsttest)");
            window.Show();
        });

        _ = RunSessionAsync(session);
        return firstStatus.Task;
    }
}
