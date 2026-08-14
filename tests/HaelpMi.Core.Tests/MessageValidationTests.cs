using HaelpMi.Core.Models;
using HaelpMi.Core.Networking.Protocol;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Deckt <see cref="MessageValidation"/> direkt ab (InternalsVisibleTo, siehe
/// HaelpMi.Core/AssemblyInfo.cs) statt nur indirekt über DiscoveryService/AlarmTcpListener -
/// das ist die eine Stelle, die untrusted Netzwerkeingaben nach dem Deserialisieren auf
/// Plausibilität prüft (CLAUDE.md: "kein Admin, keine zentrale Berechtigungsinstanz" heißt
/// nicht "kein Schutz gegen bösartige/kaputte Pakete"). Jeder Test ändert genau EIN Feld
/// gegenüber einer bekannt gültigen Basis-Nachricht, damit ein Fehlschlag eindeutig auf die
/// jeweilige Prüfung zurückzuführen ist.
/// </summary>
public class MessageValidationTests
{
    private static BootCallMessage ValidBootCall() => new(
        MessageKind.Announce,
        Guid.NewGuid(),
        Guid.NewGuid(),
        "PC-217",
        "Herr Novak",
        "Zimmer",
        "108",
        Role.User,
        false,
        AppConstants.AlarmTcpPort,
        "0.7.0",
        1,
        DateTimeOffset.UtcNow);

    private static KnownDeviceSummary ValidKnownDevice() => new(
        Guid.NewGuid(), "PC-218", "Frau Muster", "Zimmer", "109", Role.User, "192.168.1.51", AppConstants.AlarmTcpPort, DateTimeOffset.UtcNow);

    [Fact]
    public void BootCallMessage_ValidBaseline_IsPlausible() =>
        Assert.True(ValidBootCall().IsPlausible());

    [Fact]
    public void BootCallMessage_EmptyCustomerGroupId_IsRejected() =>
        Assert.False((ValidBootCall() with { CustomerGroupId = Guid.Empty }).IsPlausible());

    [Fact]
    public void BootCallMessage_EmptyDeviceId_IsRejected() =>
        Assert.False((ValidBootCall() with { DeviceId = Guid.Empty }).IsPlausible());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void BootCallMessage_InvalidTcpPort_IsRejected(int port) =>
        Assert.False((ValidBootCall() with { TcpPort = port }).IsPlausible());

    [Fact]
    public void BootCallMessage_TcpPort_BoundaryValuesAreAccepted()
    {
        Assert.True((ValidBootCall() with { TcpPort = 1 }).IsPlausible());
        Assert.True((ValidBootCall() with { TcpPort = 65535 }).IsPlausible());
    }

    [Fact]
    public void BootCallMessage_OversizedComputerName_IsRejected() =>
        Assert.False((ValidBootCall() with { ComputerName = new string('a', 201) }).IsPlausible());

    [Fact]
    public void BootCallMessage_OversizedUser_IsRejected() =>
        Assert.False((ValidBootCall() with { User = new string('a', 201) }).IsPlausible());

    [Fact]
    public void BootCallMessage_OversizedRoomName_IsRejected() =>
        Assert.False((ValidBootCall() with { RoomName = new string('a', 201) }).IsPlausible());

    [Fact]
    public void BootCallMessage_OversizedRoomNumber_IsRejected() =>
        Assert.False((ValidBootCall() with { RoomNumber = new string('a', 201) }).IsPlausible());

    [Fact]
    public void BootCallMessage_OversizedProgramVersion_IsRejected() =>
        Assert.False((ValidBootCall() with { ProgramVersion = new string('9', 41) }).IsPlausible());

    [Fact]
    public void BootCallMessage_UndefinedRoleEnumValue_IsRejected() =>
        Assert.False((ValidBootCall() with { Role = (Role)99 }).IsPlausible());

    [Fact]
    public void BootCallMessage_UndefinedKindEnumValue_IsRejected() =>
        Assert.False((ValidBootCall() with { Kind = (MessageKind)99 }).IsPlausible());

    [Fact]
    public void BootCallMessage_NullKnownDevices_IsPlausible() =>
        // Ältere Announces setzen das Feld nie (siehe Klassenkommentar auf BootCallMessage) -
        // null muss weiterhin gültig bleiben, nicht nur eine leere Liste.
        Assert.True((ValidBootCall() with { KnownDevices = null }).IsPlausible());

    [Fact]
    public void BootCallMessage_TooManyKnownDevices_IsRejected()
    {
        var tooMany = Enumerable.Range(0, 1001).Select(_ => ValidKnownDevice()).ToList();
        Assert.False((ValidBootCall() with { KnownDevices = tooMany }).IsPlausible());
    }

    [Fact]
    public void BootCallMessage_MaxAllowedKnownDevicesCount_IsPlausible()
    {
        var exactlyMax = Enumerable.Range(0, 1000).Select(_ => ValidKnownDevice()).ToList();
        Assert.True((ValidBootCall() with { KnownDevices = exactlyMax }).IsPlausible());
    }

    [Fact]
    public void BootCallMessage_KnownDeviceWithEmptyDeviceId_IsRejected()
    {
        var devices = new List<KnownDeviceSummary> { ValidKnownDevice() with { DeviceId = Guid.Empty } };
        Assert.False((ValidBootCall() with { KnownDevices = devices }).IsPlausible());
    }

    [Fact]
    public void BootCallMessage_KnownDeviceWithInvalidTcpPort_IsRejected()
    {
        var devices = new List<KnownDeviceSummary> { ValidKnownDevice() with { TcpPort = 0 } };
        Assert.False((ValidBootCall() with { KnownDevices = devices }).IsPlausible());
    }

    [Fact]
    public void BootCallMessage_KnownDeviceWithUndefinedRole_IsRejected()
    {
        var devices = new List<KnownDeviceSummary> { ValidKnownDevice() with { Role = (Role)99 } };
        Assert.False((ValidBootCall() with { KnownDevices = devices }).IsPlausible());
    }

    [Fact]
    public void BootCallMessage_KnownDeviceWithOversizedField_IsRejected()
    {
        var devices = new List<KnownDeviceSummary> { ValidKnownDevice() with { IpAddress = new string('1', 201) } };
        Assert.False((ValidBootCall() with { KnownDevices = devices }).IsPlausible());
    }

    // ------------------------------------------------------------- AlarmRequestMessage ---

    private static AlarmRequestMessage ValidAlarmRequest() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "PC-217",
        "Herr Novak",
        "Zimmer",
        "108",
        false,
        "Bitte sofort kommen!",
        2,
        DateTimeOffset.UtcNow);

    [Fact]
    public void AlarmRequestMessage_ValidBaseline_IsPlausible() =>
        Assert.True(ValidAlarmRequest().IsPlausible());

    [Fact]
    public void AlarmRequestMessage_EmptyCustomerGroupId_IsRejected() =>
        Assert.False((ValidAlarmRequest() with { CustomerGroupId = Guid.Empty }).IsPlausible());

    [Fact]
    public void AlarmRequestMessage_EmptySenderDeviceId_IsRejected() =>
        Assert.False((ValidAlarmRequest() with { SenderDeviceId = Guid.Empty }).IsPlausible());

    [Fact]
    public void AlarmRequestMessage_OversizedSenderComputerName_IsRejected() =>
        Assert.False((ValidAlarmRequest() with { SenderComputerName = new string('a', 201) }).IsPlausible());

    [Fact]
    public void AlarmRequestMessage_OversizedSenderUser_IsRejected() =>
        Assert.False((ValidAlarmRequest() with { SenderUser = new string('a', 201) }).IsPlausible());

    [Fact]
    public void AlarmRequestMessage_OversizedSenderRoomName_IsRejected() =>
        Assert.False((ValidAlarmRequest() with { SenderRoomName = new string('a', 201) }).IsPlausible());

    [Fact]
    public void AlarmRequestMessage_OversizedSenderRoomNumber_IsRejected() =>
        Assert.False((ValidAlarmRequest() with { SenderRoomNumber = new string('a', 201) }).IsPlausible());

    [Fact]
    public void AlarmRequestMessage_OversizedText_IsRejected() =>
        // 500 Zeichen (MaxAlarmTextLength) ist die Grenze - der Anzeigetext eines
        // Alarm-Profils selbst (FR-9) landet hier ungekürzt, daher großzügiger als die
        // sonst 200 Zeichen langen Namensfelder.
        Assert.False((ValidAlarmRequest() with { Text = new string('a', 501) }).IsPlausible());

    [Fact]
    public void AlarmRequestMessage_MaxAllowedTextLength_IsPlausible() =>
        Assert.True((ValidAlarmRequest() with { Text = new string('a', 500) }).IsPlausible());

    [Fact]
    public void AlarmRequestMessage_IsPlausible_ReturnsTrue_RegardlessOfIsTestValue()
    {
        // bool ist immer plausibel (analog SenderIsRemoteSession, das ebenfalls nicht
        // geprüft wird) - kein neuer Ablehnungsfall, nur Absicherung gegen eine
        // versehentliche künftige Prüfung, die IsTest fälschlich einschränkt.
        Assert.True((ValidAlarmRequest() with { IsTest = true }).IsPlausible());
        Assert.True((ValidAlarmRequest() with { IsTest = false }).IsPlausible());
    }
}
