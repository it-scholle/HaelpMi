using Xunit.Abstractions;
using Xunit.Sdk;

namespace HaelpMi.Installer.Tests;

/// <summary>Standard xUnit-Musterlösung für eine feste Testreihenfolge über <see cref="TestPriorityAttribute"/> - siehe Kommentar dort.</summary>
public sealed class PriorityOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        return testCases
            .OrderBy(testCase => testCase.TestMethod.Method
                .GetCustomAttributes(typeof(TestPriorityAttribute).AssemblyQualifiedName)
                .FirstOrDefault()?.GetNamedArgument<int>(nameof(TestPriorityAttribute.Priority)) ?? int.MaxValue)
            .ThenBy(testCase => testCase.TestMethod.Method.Name);
    }
}
