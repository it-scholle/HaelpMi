using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Issue #59/#60-Nachtrag (Nutzerbericht 07.09.2026): ein Testaufbau mit einer Custom-2-
/// Lizenz plus 3 weiteren Clients blieb komplett unblockiert, weil (a) ein Gerät ohne jede
/// erkennbare Lizenz bisher als "unbegrenzt" statt als "0 Plätze" behandelt wurde und (b)
/// unklar war, ob das Dashboard selbst je zum Kontingent zählen sollte. Deckt beide Fixe ab.
/// </summary>
public class LicenseLimitGuardTests
{
    private static LiveIdentity MakeIdentity(Guid deviceId, Role role, DateTimeOffset firstSeenUtc) =>
        new(Guid.NewGuid(), deviceId, "PC", "User", "Raum", "1", role, false, "1.0.0", 0, firstSeenUtc);

    private static LicenseCheckResult MissingLicense() => new(LicenseStatus.Missing, null);
    private static LicenseCheckResult InvalidLicense() => new(LicenseStatus.Invalid, null);

    private static LicenseCheckResult ValidLicense(int? userLimit, LicenseStatus status = LicenseStatus.Valid) =>
        new(status, new License(Guid.NewGuid(), LicenseTier.Custom, userLimit, DateTime.UtcNow, DateTime.UtcNow.AddYears(1), "irrelevant"));

    [Fact]
    public void IsOwnDeviceDisabled_True_ForUserRoleDevice_WhenLicenseIsMissing()
    {
        var identity = MakeIdentity(Guid.NewGuid(), Role.User, DateTimeOffset.UtcNow);
        var guard = new LicenseLimitGuard(() => identity, MissingLicense, () => new List<DeviceEntry>());

        Assert.True(guard.IsOwnDeviceDisabled());
    }

    [Fact]
    public void IsOwnDeviceDisabled_True_ForUserRoleDevice_WhenLicenseIsInvalid()
    {
        var identity = MakeIdentity(Guid.NewGuid(), Role.User, DateTimeOffset.UtcNow);
        var guard = new LicenseLimitGuard(() => identity, InvalidLicense, () => new List<DeviceEntry>());

        Assert.True(guard.IsOwnDeviceDisabled());
    }

    [Fact]
    public void IsOwnDeviceDisabled_False_ForAdminRoleDevice_EvenWhenLicenseIsMissing()
    {
        // Das Dashboard darf sich nie selbst aussperren - sonst gäbe es keinen Weg mehr,
        // eine fehlende/kaputte Lizenz überhaupt zu reparieren.
        var identity = MakeIdentity(Guid.NewGuid(), Role.Admin, DateTimeOffset.UtcNow);
        var guard = new LicenseLimitGuard(() => identity, MissingLicense, () => new List<DeviceEntry>());

        Assert.False(guard.IsOwnDeviceDisabled());
    }

    [Fact]
    public void IsOwnDeviceDisabled_False_ForAdminRoleDevice_EvenWhenOverLimit()
    {
        var adminId = Guid.NewGuid();
        var identity = MakeIdentity(adminId, Role.Admin, DateTimeOffset.UtcNow);
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = Guid.NewGuid(), Role = Role.User, FirstSeenUtc = DateTimeOffset.UtcNow.AddSeconds(1) },
        };
        var guard = new LicenseLimitGuard(() => identity, () => ValidLicense(0), () => devices);

        Assert.False(guard.IsOwnDeviceDisabled());
    }

    [Fact]
    public void GetDisabledDeviceIds_NeverContainsAdminDevices()
    {
        var adminDeviceId = Guid.NewGuid();
        var identity = MakeIdentity(Guid.NewGuid(), Role.User, DateTimeOffset.UtcNow);
        var devices = new List<DeviceEntry>
        {
            new() { DeviceId = adminDeviceId, Role = Role.Admin, FirstSeenUtc = DateTimeOffset.UtcNow.AddDays(-1) },
        };
        var guard = new LicenseLimitGuard(() => identity, () => ValidLicense(0), () => devices);

        Assert.DoesNotContain(adminDeviceId, guard.GetDisabledDeviceIds());
    }

    [Fact]
    public void IsOwnDeviceDisabled_False_ForUserRoleDevice_WhenValidLicenseCoversIt()
    {
        var identity = MakeIdentity(Guid.NewGuid(), Role.User, DateTimeOffset.UtcNow);
        var guard = new LicenseLimitGuard(() => identity, () => ValidLicense(1), () => new List<DeviceEntry>());

        Assert.False(guard.IsOwnDeviceDisabled());
    }

    [Fact]
    public void IsOwnDeviceDisabled_UsesRealUserLimit_ForExpiredButOnceValidLicense()
    {
        // Soft-Expiry (CLAUDE.md): eine abgelaufene Lizenz sperrt nicht automatisch alles -
        // ihr tatsächliches UserLimit bleibt maßgeblich, anders als bei Missing/Invalid.
        var identity = MakeIdentity(Guid.NewGuid(), Role.User, DateTimeOffset.UtcNow);
        var guard = new LicenseLimitGuard(() => identity, () => ValidLicense(1, LicenseStatus.Expired), () => new List<DeviceEntry>());

        Assert.False(guard.IsOwnDeviceDisabled());
    }

    [Fact]
    public void IsOwnDeviceDisabled_True_ForNewestUserDevice_WhenCustomTwoDeviceLimitIsExceeded()
    {
        // Nachstellung des gemeldeten Testaufbaus: Custom-Lizenz mit 2 Nutzern, Dashboard
        // (Admin) zählt nicht mit, 3 User-Geräte kämpfen um 2 Plätze - die beiden ältesten
        // bleiben aktiv, das jüngste wird deaktiviert.
        var now = DateTimeOffset.UtcNow;
        var oldestId = Guid.NewGuid();
        var middleId = Guid.NewGuid();
        var newestId = Guid.NewGuid();
        var admin = new DeviceEntry { DeviceId = Guid.NewGuid(), Role = Role.Admin, FirstSeenUtc = now.AddDays(-10) }; // Dashboard, zählt nicht mit
        var oldest = new DeviceEntry { DeviceId = oldestId, Role = Role.User, FirstSeenUtc = now.AddDays(-3) };
        var middle = new DeviceEntry { DeviceId = middleId, Role = Role.User, FirstSeenUtc = now.AddDays(-2) };
        var newest = new DeviceEntry { DeviceId = newestId, Role = Role.User, FirstSeenUtc = now.AddDays(-1) };

        LicenseCheckResult License() => ValidLicense(2);

        // Jedes Gerät sieht in seinem eigenen DeviceStore nur die JEWEILS ANDEREN (nie sich
        // selbst, siehe DeviceStore-Klassenkommentar "kein Gerät trägt sich selbst ein").
        var oldestGuard = new LicenseLimitGuard(() => MakeIdentity(oldestId, Role.User, now.AddDays(-3)), License, () => new List<DeviceEntry> { admin, middle, newest });
        var middleGuard = new LicenseLimitGuard(() => MakeIdentity(middleId, Role.User, now.AddDays(-2)), License, () => new List<DeviceEntry> { admin, oldest, newest });
        var newestGuard = new LicenseLimitGuard(() => MakeIdentity(newestId, Role.User, now.AddDays(-1)), License, () => new List<DeviceEntry> { admin, oldest, middle });

        Assert.False(oldestGuard.IsOwnDeviceDisabled());
        Assert.False(middleGuard.IsOwnDeviceDisabled());
        Assert.True(newestGuard.IsOwnDeviceDisabled());
    }
}
