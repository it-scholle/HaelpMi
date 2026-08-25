using HaelpMi.Core.Ipc;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Issue #9: ohne Sitzungsbezug im Pipe-Namen würde Config bei Fast User Switching
/// nicht-deterministisch mit dem Agent einer FREMDEN Sitzung statt der eigenen sprechen.
/// </summary>
public class IpcPipeNamingTests
{
    [Fact]
    public void BuildSessionScopedPipeName_DifferentSessionIds_ProduceDifferentNames()
    {
        var nameForSessionOne = IpcPipeNaming.BuildSessionScopedPipeName(1);
        var nameForSessionTwo = IpcPipeNaming.BuildSessionScopedPipeName(2);

        Assert.NotEqual(nameForSessionOne, nameForSessionTwo);
    }

    [Fact]
    public void BuildSessionScopedPipeName_SameSessionId_IsStable()
    {
        Assert.Equal(IpcPipeNaming.BuildSessionScopedPipeName(3), IpcPipeNaming.BuildSessionScopedPipeName(3));
    }
}
