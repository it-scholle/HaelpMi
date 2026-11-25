using HaelpMi.Core.Audio;
using HaelpMi.Core.Interop;
using HaelpMi.Core.Licensing;
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
    private readonly LicenseLimitGuard _licenseLimitGuard;
    private readonly ReceivedAlarmSessions<AlarmPopupWindow> _receivedSessions = new(AppConstants.AlarmAutoCloseAfterLastSignal);

    public AlarmFlowCoordinator(
        Func<LiveIdentity> identityProvider,
        Func<OwnSettings> settingsProvider,
        Func<SharedConfig> sharedConfigProvider,
        Func<LicenseCheckResult> licenseProvider,
        AlarmFeedbackChannel feedbackChannel)
    {
        _identityProvider = identityProvider;
        _settingsProvider = settingsProvider;
        _sharedConfigProvider = sharedConfigProvider;
        _feedbackChannel = feedbackChannel;
        _sender = new AlarmSender(_auditLog.Append);
        _audioPlayer = new MultiDeviceAlarmPlayer(_auditLog.Append);
        _licenseLimitGuard = new LicenseLimitGuard(identityProvider, licenseProvider, _deviceStore.Load);
        _feedbackChannel.StatusRelayReceived += (_, relay) => HandleStatusRelay(relay);
    }

    /// <summary>Issue #59/#60: true, solange dieses Gerät wegen Lizenzüberschreitung deaktiviert ist.</summary>
    public bool IsOwnDeviceLicenseDisabled() => _licenseLimitGuard.IsOwnDeviceDisabled();

    public LicenseBlockReason GetOwnLicenseBlockReason() => _licenseLimitGuard.GetOwnBlockReason();

    /// <summary>
    /// Issue #61-Nachtrag ("Löschen deaktiviert nicht wirklich"): true, sobald ein Admin
    /// dieses Gerät im Geräte-Tab gelöscht hat (siehe OwnSettings.Removed) - anders als
    /// <see cref="IsOwnDeviceLicenseDisabled"/> nie wieder rückgängig zu machen, außer
    /// durch eine echte Neuinstallation.
    /// </summary>
    public bool IsOwnDeviceRemoved() => _settingsProvider().Removed;

    /// <summary>Called from the TCP listener's background thread when an alarm arrives (FR-9/FR-47).</summary>
    public void HandleIncomingAlarmRequest(AlarmReceivedEventArgs args)
    {
        var request = args.Request;

        // Bei Fast User Switching bekommt JEDE angemeldete Sitzung ein empfangenes
        // Alarmsignal (siehe AlarmChannel), auch eine gerade weggeschaltete. Ein NEUES erzwungenes Popup dort kann niemand sehen oder
        // wegklicken - lieber gar nicht erst zeigen, als eine Zustellung vortäuschen, die
        // niemand wahrnimmt. Die Quittung an den Sender folgt der Anzeige (ReportDisplayed):
        // sie geht nur raus, wenn diese oder eine andere Sitzung der Maschine das Popup zeigt
        // (siehe AlarmTcpListener, AlarmChannel). Ein schon offenes Popup (aus der Zeit, als
        // diese Sitzung noch aktiv war) wird trotzdem weiter aktualisiert - das erzeugt kein
        // neues Popup.
        var sessionVisible = ActiveSessionDetector.IsCurrentSessionActive();
        IncomingAlarmAction? action = null;

        // Schneller Ausstieg ohne UI-Thread: hängt der Dispatcher, liefe das Invoke unten ins
        // Zeitlimit und eine Welle einer längst abgeschlossenen Session löste trotzdem den Ton
        // aus. Maßgeblich bleibt die Entscheidung im Dispatcher.
        if (_receivedSessions.IsCompleted(request.AlarmSessionId))
        {
            _auditLog.Append($"alarm-Welle für bereits abgeschlossene Session ignoriert: alarmSessionId={request.AlarmSessionId}");
            args.ReportDisplayed(true); // schon gesehen und erledigt
            return;
        }

        var settings = _settingsProvider();
        var soundOption = IncomingSoundCatalog.Resolve(settings.IncomingSoundId);

        // Timeout statt unbegrenztem Invoke - direkt nach Logon ist der Dispatcher (siehe
        // AlarmDispatcherReadyTimeout) unter Umständen noch nicht am Pumpen, sonst würde dieser
        // Aufruf den Empfangsthread bis zum Ende von OnStartup blockieren und der Ack käme beim
        // Sender nie rechtzeitig an. Achtung: Startet die Aktion nicht innerhalb des Zeitlimits,
        // verwirft WPF sie - diese Welle zeigt dann kein Fenster, nur den Ton; erst die nächste
        // Repeat-Welle öffnet es. Läuft der Dispatcher (Normalfall), wie ein normales Invoke.
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // Entscheidung erst hier auf dem UI-Thread: ein gerade laufendes Schließen ist dann
            // vollständig verbucht, und eine Welle derselben Session öffnet kein zweites Fenster.
            action = _receivedSessions.Decide(request.AlarmSessionId, sessionVisible, out var existing);
            switch (action)
            {
                case IncomingAlarmAction.IgnoreCompletedSession:
                    _auditLog.Append($"alarm-Welle für bereits abgeschlossene Session ignoriert: alarmSessionId={request.AlarmSessionId}");
                    return;
                case IncomingAlarmAction.SuppressInactiveSession:
                    _auditLog.Append($"alarm popup unterdrückt (Sitzung nicht aktiv sichtbar): alarmSessionId={request.AlarmSessionId}");
                    return;
                case IncomingAlarmAction.RefreshOpenPopup:
                    // Same alarm, still on screen - just refresh it (FR-52), don't spam a
                    // second window for every 5-second repeat.
                    existing!.NotifyNewSignalReceived(request.SentAtUtc);
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
            popup.Closed += (_, _) => _receivedSessions.PopupClosed(request.AlarmSessionId, popup.IsCloseAllowed);

            _receivedSessions.PopupShown(request.AlarmSessionId, popup);
            popup.Show();
        }, AppConstants.AlarmDispatcherReadyTimeout);

        // Ohne Aktion hat der Dispatcher nicht rechtzeitig angefangen: kein Fenster in dieser Welle.
        args.ReportDisplayed(action is not (null or IncomingAlarmAction.SuppressInactiveSession));

        if (action is IncomingAlarmAction.IgnoreCompletedSession or IncomingAlarmAction.SuppressInactiveSession)
        {
            return;
        }

        _ = _audioPlayer.PlayOnAllActiveDevicesAsync(soundOption);
    }

    /// <summary>Every aggregated status update from the sender (FR-51): keeps a still-open receiver popup's threshold gate current.</summary>
    private void HandleStatusRelay(AlarmStatusRelayMessage relay)
    {
        var popup = _receivedSessions.StatusRelayReceived(relay.AlarmSessionId, relay.SenderStillSending);
        if (popup is null)
        {
            return;
        }

        // Issue #92: gleicher Grund/gleiche Frist wie in HandleIncomingAlarmRequest oben -
        // ein Status-Relay kann ebenso in das Startup-Fenster vor Dispatcher.Run() fallen.
        System.Windows.Application.Current.Dispatcher.Invoke(
            () => popup.UpdateOnTheWayCount(relay.OnTheWayUserNames.Count, relay.SenderStillSending),
            AppConstants.AlarmDispatcherReadyTimeout);
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
        var removed = IsOwnDeviceRemoved();
        var blockReason = _licenseLimitGuard.GetOwnBlockReason();
        if (SendBlockNotice.For(removed, blockReason) is { } notice)
        {
            _auditLog.Append($"alarm not sent alarmProfileId={profile.Id} removed={removed} blockReason={blockReason}");
            BlockedHintDialog.Show(notice);
            return;
        }

        var identity = _identityProvider();
        var devices = _deviceStore.Load();
        var groups = _sharedConfigProvider().DeviceGroups;
        var targets = RecipientResolver.ResolveRecipientsForSender(profile, identity.DeviceId, identity.RoomNumber, devices, groups, excludeSender: true);

        // Issue #60: lizenzüberschrittene Empfänger sind für andere unerreichbar (siehe
        // AlarmTcpListener) - sie erst gar nicht anschreiben, statt einen strukturell nie
        // bestätigbaren Verbindungsversuch zu zählen.
        //
        // Issue #61-Nachtrag ("Deinstalliert" bleibt seit der Neufassung sichtbar in
        // devices.json statt wie zuvor entfernt zu werden, siehe DeviceEntry.Removed) -
        // ohne diesen zusätzlichen Filter würde ein deinstalliertes Gerät weiterhin als
        // Empfänger angeschrieben (derselbe "0/1 empfangen, obwohl längst deaktiviert"-Bug
        // wie beim ursprünglichen Fehlerbericht, nur diesmal weil es nicht mehr aus der
        // Liste verschwindet statt weil die Löschung nie ankam).
        var disabledDeviceIds = _licenseLimitGuard.GetDisabledDeviceIds();
        targets = targets.Where(t => !disabledDeviceIds.Contains(t.DeviceId) && !t.Removed).ToList();

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
        if (IsOwnDeviceRemoved() || _licenseLimitGuard.IsOwnDeviceDisabled())
        {
            return Task.FromResult(false);
        }

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
