using HaelpMi.Core.Models;
using HaelpMi.Core.Security;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// P1-Notfall-Schalter (19.08.2026, siehe EncryptionDebugSwitch/DeploymentInfo-Klassendoku) -
/// deckt nur die eine Zeile ab, auf der alles andere aufbaut: mit gesetzter Umgebungsvariable
/// liefert <see cref="DeploymentInfo.EffectiveGroupKeyBase64"/> immer <c>null</c>, unabhängig
/// vom tatsächlich installierten Schlüssel - jeder der sieben SecureEnvelope-Aufrufer fällt
/// dadurch bereits über sein bestehendes "kein Gruppenschlüssel" -Verhalten auf Klartext
/// zurück (siehe SecureEnvelopeCodec/AlarmChannelEncryptionTests), kein zusätzlicher Test
/// dafür hier nötig.
/// </summary>
public class EncryptionDebugSwitchTests
{
    private const string EnvVarName = "DISABLE_ENCRYPTION_DEBUG_ONLY";

    public EncryptionDebugSwitchTests()
    {
        // Tests dürfen sich nicht gegenseitig über die Prozessumgebung beeinflussen -
        // xUnit kann Tests derselben Klasse parallel/in beliebiger Reihenfolge ausführen.
        Environment.SetEnvironmentVariable(EnvVarName, null);
    }

    [Fact]
    public void IsDisabled_DefaultsToFalse_WhenEnvVarNotSet()
    {
        Assert.False(EncryptionDebugSwitch.IsDisabled);
    }

    [Fact]
    public void IsDisabled_TrueOnlyForExactValueOne()
    {
        Environment.SetEnvironmentVariable(EnvVarName, "true");
        Assert.False(EncryptionDebugSwitch.IsDisabled); // kein Fuzzy-Match - nur "1" zaehlt

        Environment.SetEnvironmentVariable(EnvVarName, "1");
        Assert.True(EncryptionDebugSwitch.IsDisabled);

        Environment.SetEnvironmentVariable(EnvVarName, null);
    }

    [Fact]
    public void DeploymentInfo_EffectiveGroupKeyBase64_ReturnsRealKey_WhenSwitchInactive()
    {
        var deployment = new DeploymentInfo { CustomerGroupId = Guid.NewGuid(), Role = Role.User, GroupKeyBase64 = "echter-schluessel" };
        Assert.Equal("echter-schluessel", deployment.EffectiveGroupKeyBase64);
    }

    [Fact]
    public void DeploymentInfo_EffectiveGroupKeyBase64_ReturnsNull_WhenSwitchActive_EvenWithRealKeyPresent()
    {
        Environment.SetEnvironmentVariable(EnvVarName, "1");
        try
        {
            var deployment = new DeploymentInfo { CustomerGroupId = Guid.NewGuid(), Role = Role.User, GroupKeyBase64 = "echter-schluessel" };
            Assert.Null(deployment.EffectiveGroupKeyBase64);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, null);
        }
    }
}
