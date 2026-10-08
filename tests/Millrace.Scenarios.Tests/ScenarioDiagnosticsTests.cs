using Millrace.Configuration;

namespace Millrace.Scenarios.Tests;

public class ScenarioDiagnosticsTests
{
    [Fact]
    public void TheTableListsEveryCodeOnceInOrder()
    {
        string[] codes = ScenarioDiagnostics.All.Select(d => d.Code).ToArray();

        Assert.Equal(7, codes.Length);
        Assert.Equal("MR200", codes[0]);
        Assert.Equal("MR206", codes[^1]);
        Assert.Equal(codes.Order(StringComparer.Ordinal), codes);
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ScenarioDiagnostics.All, d =>
        {
            Assert.EndsWith(".", d.Explanation, StringComparison.Ordinal);
            Assert.False(d.Title.EndsWith('.'));
        });
    }

    [Fact]
    public void ScenarioCodesDoNotCollideWithConfigurationCodes()
    {
        var configuration = ConfigDiagnostics.All.Select(d => d.Code).ToHashSet(StringComparer.Ordinal);

        Assert.All(ScenarioDiagnostics.All, d => Assert.DoesNotContain(d.Code, configuration));
    }
}
