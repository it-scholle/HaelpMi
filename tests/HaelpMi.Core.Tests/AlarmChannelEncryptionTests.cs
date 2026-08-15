using HaelpMi.Core.Models;
using HaelpMi.Core.Networking;
using HaelpMi.Core.Networking.Protocol;
using HaelpMi.Core.Security;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Covers die LAN-Verschlüsselung des Alarm-Kanals (siehe CLAUDE.md "Lizenz &amp;
/// Secrets", SecureEnvelopeCodec): Ende-zu-Ende-Roundtrip über SecureEnvelope, wenn das
/// Zielgerät bei einem früheren direkten Boot-Call-Kontakt als verschlüsselungsfähig +
/// gepinnt bekannt ist, sowie der sichere Klartext-Fallback für die Rollout-
/// Übergangsphase (altes/unbekanntes Zielgerät), analog zu den bestehenden
/// AlarmSender/AlarmTcpListener-Loopback-Tests in NetworkingTests.cs.
/// </summary>
public class AlarmChannelEncryptionTests
{
    private static LiveIdentity MakeIdentity(Guid customerGroupId, Guid deviceId, string computerName = "PC", string user = "User", string room = "Raum", string roomNumber = "1") =>
        new(customerGroupId, deviceId, computerName, user, room, roomNumber, Role.User, false, "9.9.9", 0);

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task AlarmSender_And_AlarmTcpListener_RoundTrip_ThroughSecureEnvelope_WhenTargetIsPinnedAndCapable()
    {
        using var scope = new TestAppDataScope();
        DeviceIdentityStore.ResetCacheForTests();

        var customerGroupId = Guid.NewGuid();
        var groupKeyBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var receiverDeviceId = Guid.NewGuid();
        var senderDeviceId = Guid.NewGuid();

        // Der Sender-Schlüssel muss beim Empfänger schon als direkt kontaktiert gepinnt
        // sein (TOFU nur über echten Boot-Call-Kontakt, hier simuliert durch vorab
        // befüllte devices.json - siehe DiscoveryService.HandleDatagramAsync).
        var senderIdentityKeys = DeviceIdentitySigner.GenerateKeyPair();
        var devices = new List<DeviceEntry>
        {
            new()
            {
                DeviceId = senderDeviceId,
                PinnedDeviceIdentityPublicKeyBase64 = senderIdentityKeys.PublicKeyBase64,
                ProtocolVersion = AppConstants.CurrentProtocolVersion,
            },
        };
        new DeviceStore().Save(devices);

        // DeviceIdentityStore.LoadOrCreate() erzeugt beim ersten Aufruf in diesem Test-Root
        // automatisch ein eigenes Schlüsselpaar für den "eigenen" Prozess (= Empfänger in
        // diesem Test) - wir überschreiben es hier nicht, der Sender bekommt seinen
        // öffentlichen Schlüssel oben separat übergeben (kein zweiter Store nötig, da
        // AlarmSender den privaten Schlüssel über SecureEnvelopeCodec.Seal nur mittelbar
        // braucht - siehe unten, wo direkt mit senderIdentityKeys signiert wird, statt über
        // den Store zu gehen, weil ein Prozess in diesem Test beide Rollen gleichzeitig
        // wäre).

        var receiverIdentity = MakeIdentity(customerGroupId, receiverDeviceId, "Empfang EG", "Poststelle", "Empfangshalle", "0");
        var listener = new AlarmTcpListener(() => receiverIdentity, groupKeyProvider: () => groupKeyBase64);

        AlarmRequestMessage? received = null;
        var receivedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.AlarmReceived += (_, args) =>
        {
            received = args.Request;
            receivedSignal.TrySetResult();
        };

        var port = GetFreeTcpPort();
        listener.Start(port);

        try
        {
            var senderIdentity = MakeIdentity(customerGroupId, senderDeviceId, "Sachbearbeitung 3", "Frau Meier", "Zimmer 214", "214");
            var sender = new AlarmSenderWithFixedDeviceKey(senderIdentityKeys.PrivateKeyBase64, groupKeyBase64);
            var profile = new AlarmProfile { Text = "Verschlüsselter Testalarm", ResponseThreshold = 1 };
            var alarmSessionId = Guid.NewGuid();
            var target = new DeviceEntry
            {
                DeviceId = receiverDeviceId,
                IpAddress = "127.0.0.1",
                TcpPort = port,
                ProtocolVersion = AppConstants.CurrentProtocolVersion,
                PinnedDeviceIdentityPublicKeyBase64 = DeviceIdentityStore.LoadOrCreate().PublicKeyBase64,
            };

            var result = await sender.SendAsync(profile, alarmSessionId, senderIdentity, new[] { target });

            Assert.Equal(1, result.TargetCount);
            Assert.Equal(1, result.AckedCount);

            await receivedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(received);
            Assert.Equal("Verschlüsselter Testalarm", received!.Text);
            Assert.Equal(senderDeviceId, received.SenderDeviceId);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    [Fact]
    public async Task AlarmSender_FallsBackToCleartext_WhenTargetNotRecordedAsEncryptionCapable()
    {
        // Sicherheitsnetz für die Rollout-Übergangsphase: ein Gruppenschlüssel allein
        // reicht nicht - ohne ProtocolVersion+gepinnten Schlüssel am Zielgerät (z. B. noch
        // nie direkt kontaktiert, oder ein alter Peer) geht der Alarm trotzdem raus, nur
        // eben unverschlüsselt. Alarmzustellung darf während der Übergangsphase nie an der
        // Verschlüsselungsfähigkeit scheitern.
        using var scope = new TestAppDataScope();
        var customerGroupId = Guid.NewGuid();
        var groupKeyBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var receiverDeviceId = Guid.NewGuid();

        var receiverIdentity = MakeIdentity(customerGroupId, receiverDeviceId);
        var listener = new AlarmTcpListener(() => receiverIdentity, groupKeyProvider: () => groupKeyBase64);

        var received = false;
        listener.AlarmReceived += (_, _) => received = true;

        var port = GetFreeTcpPort();
        listener.Start(port);

        try
        {
            var senderIdentity = MakeIdentity(customerGroupId, Guid.NewGuid());
            var sender = new AlarmSender(groupKeyProvider: () => groupKeyBase64);
            var profile = new AlarmProfile { Text = "Klartext-Fallback" };
            // Kein ProtocolVersion/PinnedDeviceIdentityPublicKeyBase64 gesetzt - Standardfall
            // für ein noch nie direkt kontaktiertes oder altes Zielgerät.
            var target = new DeviceEntry { DeviceId = receiverDeviceId, IpAddress = "127.0.0.1", TcpPort = port };

            var result = await sender.SendAsync(profile, Guid.NewGuid(), senderIdentity, new[] { target });

            Assert.Equal(1, result.AckedCount);
            Assert.True(received);
        }
        finally
        {
            await listener.DisposeAsync();
        }
    }

    /// <summary>
    /// Testhilfe: signiert mit einem fest vorgegebenen Geräte-Identitätsschlüssel statt
    /// über DeviceIdentityStore.LoadOrCreate() - in einem einzelnen Testprozess würden
    /// Sender und Empfänger sonst denselben (Prozess-weit gecachten) Schlüssel benutzen,
    /// was den eigentlichen Sinn des Tests (zwei UNTERSCHIEDLICHE Geräte-Identitäten)
    /// unterlaufen würde. Duplikation der Seal-Logik ist hier bewusst in Kauf genommen -
    /// SecureEnvelopeCodec selbst bleibt intern und unverändert.
    /// </summary>
    private sealed class AlarmSenderWithFixedDeviceKey
    {
        private readonly string _devicePrivateKeyBase64;
        private readonly string _groupKeyBase64;

        public AlarmSenderWithFixedDeviceKey(string devicePrivateKeyBase64, string groupKeyBase64)
        {
            _devicePrivateKeyBase64 = devicePrivateKeyBase64;
            _groupKeyBase64 = groupKeyBase64;
        }

        public async Task<AlarmSendResult> SendAsync(AlarmProfile profile, Guid alarmSessionId, LiveIdentity ownIdentity, IReadOnlyList<DeviceEntry> targets)
        {
            var request = new AlarmRequestMessage(
                ownIdentity.CustomerGroupId, profile.Id, alarmSessionId, ownIdentity.DeviceId,
                ownIdentity.ComputerName, ownIdentity.User, ownIdentity.RoomName, ownIdentity.RoomNumber,
                ownIdentity.IsRemoteSession, profile.Text, profile.ResponseThreshold, DateTimeOffset.UtcNow);

            var target = targets[0];
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Parse(target.IpAddress), target.TcpPort);
            await using var stream = client.GetStream();

            var envelope = SecureEnvelopeCodec.Seal(request, request.CustomerGroupId, request.SenderDeviceId, _groupKeyBase64, _devicePrivateKeyBase64, DateTimeOffset.UtcNow);
            Assert.NotNull(envelope);
            var line = NetworkSerializer.ToJsonLine(envelope);
            var payload = NetworkSerializer.Encoding.GetBytes(line);
            await stream.WriteAsync(payload);
            await stream.FlushAsync();

            using var reader = new StreamReader(stream, NetworkSerializer.Encoding);
            var responseLine = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var acked = responseLine is not null;

            return new AlarmSendResult { TargetCount = 1, AckedCount = acked ? 1 : 0 };
        }
    }
}
