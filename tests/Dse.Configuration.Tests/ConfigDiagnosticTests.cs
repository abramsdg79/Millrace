namespace Dse.Configuration.Tests;

public class ConfigDiagnosticTests
{
    [Fact]
    public void FormatsAsCodePathMessageFix()
    {
        var diagnostic = new ConfigDiagnostic(
            "DSE104", DiagnosticSeverity.Error, "$.components[2].parameters.belt",
            "'CV01' is not a component in this plant.", "Use one of CV001, CV002 — 'CV001' is closest.");

        Assert.Equal(
            "DSE104 $.components[2].parameters.belt\n  'CV01' is not a component in this plant.\n  Fix: Use one of CV001, CV002 — 'CV001' is closest.",
            diagnostic.ToText());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ADiagnosticWithoutAFixCannotBeMade(string fix)
    {
        Assert.Throws<ArgumentException>(
            () => new ConfigDiagnostic("DSE103", DiagnosticSeverity.Error, "$", "Something is wrong.", fix));
    }

    [Fact]
    public void TheTableListsEveryCodeOnceInOrder()
    {
        string[] codes = ConfigDiagnostics.All.Select(d => d.Code).ToArray();

        Assert.Equal(16, codes.Length);
        Assert.Equal("DSE100", codes[0]);
        Assert.Equal("DSE115", codes[^1]);
        Assert.Equal(codes.Order(StringComparer.Ordinal), codes);
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.All(ConfigDiagnostics.All, d =>
        {
            Assert.EndsWith(".", d.Explanation, StringComparison.Ordinal);
            Assert.False(d.Title.EndsWith('.'));
        });
    }
}
