using System.Collections.Concurrent;
using HaelpMi.Core.Audio;
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

    /// <summary>Testmodus-Toggle (Nutzerwunsch 13.08.2026), scharfgeschaltet per IPC vom Konfigurator aus - siehe TestModeArmState-Klassendoku für die Sicherheitsbegründung des Zeitstempel-Ansatzes.</summary>
    private readonly TestModeArmState _testModeArmState = new();

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
                request.SentAtUtc,
                request.IsTest);

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
        // Testmodus-Toggle: verbraucht die Scharfschaltung beim Trigger-VERSUCH, nicht erst
        // beim erfolgreichen Versand - "gilt für den nächsten Hotkey-Trigger" (Nutzerwunsch
        // 13.08.2026), auch wenn unten z.B. wegen leerem Empfängerkreis nichts verschickt wird.
        var isTest = _testModeArmState.TryConsume(DateTimeOffset.UtcNow);

        var identity = _identityProvider();
        var devices = _deviceStore.Load();
        var groups = _sharedConfigProvider().DeviceGroups;
        var targets = RecipientResolver.ResolveRecipientsForSender(profile, identity.DeviceId, identity.RoomNumber, devices, groups);
        if (targets.Count == 0)
        {
            return; // nothing to send, nothing to show (Teil 2, Abschnitt 4: an empty recipient set is a valid, if useless, admin configuration)
        }

        var session = new RepeatingAlarmSession(profile, targets, identity, _sender, _feedbackChannel, DateTimeOffset.UtcNow, isTest);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new SenderStatusWindow(session, profile.Name);
            window.Show();
        });

        _ = RunSessionAsync(session);
    }

    /// <summary>Scharfschalten des Testmodus-Toggles (Konfigurator-IPC, "ArmTestMode").</summary>
    public void ArmTestModeOnce() => _testModeArmState.Arm(DateTimeOffset.UtcNow);

    /// <summary>Manuelles Wieder-Ausschalten (Konfigurator-IPC, "DisarmTestMode") - reiner UX-Komfort, siehe TestModeArmState-Klassendoku.</summary>
    public void DisarmTestMode() => _testModeArmState.Disarm();

    /// <summary>Für die Konfigurator-Countdown-Anzeige ("TestModeStatus"-IPC) - null, falls gerade nicht scharf.</summary>
    public TimeSpan? TestModeRemaining => _testModeArmState.Remaining(DateTimeOffset.UtcNow);

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
    /// "Testalarm an mich selbst senden" (FR-27): eine echte <see cref="RepeatingAlarmSession"/>
    /// mit sich selbst als einzigem Ziel, statt nur eines einzelnen Sende-Waves. Nutzervorgabe
    /// 13.08.2026: der Selbsttest braucht immer nur die eigene Bestätigung und muss sich danach
    /// sofort schließen lassen - unabhängig vom im Profil konfigurierten Schwellwert. Ein reiner
    /// Einzel-Send (vorherige Fassung) hängte das Testfenster bis zum unbedingten 1-Minuten-
    /// Auto-Close fest, weil niemand auf die eigene "bin unterwegs"-Antwort lauschte: nur eine
    /// laufende RepeatingAlarmSession hört auf AlarmFeedbackChannel.OnMyWayReceived und schickt
    /// danach den Status-Relay, der AlarmPopupWindow.UpdateOnTheWayCount (und damit den
    /// Schließen-Button) freischaltet.
    /// </summary>
    public async Task<bool> SendSelfTestAsync(AlarmProfile profile)
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

        // Schwellwert für den Selbsttest hart auf 1 überschreiben (siehe Kommentar oben) - nur
        // Id/Text werden auf diesem Pfad überhaupt gelesen (RepeatingAlarmSession/AlarmSender),
        // RecipientAssignments/Hotkey/Name sind hier irrelevant, da das Ziel explizit selfTarget
        // ist statt über den RecipientResolver aufgelöst zu werden.
        var selfTestProfile = new AlarmProfile
        {
            Id = profile.Id,
            Text = profile.Text,
            ResponseThreshold = 1,
        };

        var session = new RepeatingAlarmSession(selfTestProfile, new[] { selfTarget }, identity, _sender, _feedbackChannel, DateTimeOffset.UtcNow);

        // Nur auf die erste Ping-Welle warten (für den IPC-Rückgabewert "hat's angekommen?"),
        // danach läuft die Session wie bei TriggerAlarmProfile im Hintergrund weiter, um auf die
        // eigene Bestätigung zu warten und den Relay zu schicken, der das Fenster freischaltet.
        var firstWaveAcked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFirstStatus(object? _, AlarmSessionStatus status)
        {
            session.StatusChanged -= OnFirstStatus;
            firstWaveAcked.TrySetResult(status.AckedCount > 0);
        }
        session.StatusChanged += OnFirstStatus;

        _ = RunSessionAsync(session);

        return await firstWaveAcked.Task;
    }
}
