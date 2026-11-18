using Xunit;

namespace HaelpMi.E2E.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public class E2ESammlung
{
    public const string Name = "E2E";
}
