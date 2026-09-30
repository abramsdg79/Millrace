using System.Text.RegularExpressions;

namespace Dse.Samples.Tests;

/// <summary>
/// The sample's README quotes each scenario's log in a block fenced as
/// <c>```text expected/&lt;name&gt;.log</c>. Every quoted line must be a whole
/// line of that golden, so the README cannot drift from what the plant does.
/// </summary>
public partial class SampleReadmeTests
{
    [GeneratedRegex(@"^```text (expected/[a-z-]+\.log)\n(.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex QuotedBlock();

    private static string Readme => File.ReadAllText(Sample.Readme).ReplaceLineEndings("\n");

    [Fact]
    public void EveryScenarioHasOneQuotedBlockAndItsCommand()
    {
        string readme = Readme;
        string[] quoted = QuotedBlock().Matches(readme).Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(Sample.Names.Select(n => $"expected/{n}.log"), quoted);
        Assert.All(Sample.Names, name => Assert.Contains(
            $"run samples/mine-conveyors/scenarios/{name}.json --expect samples/mine-conveyors/expected/{name}.log",
            readme,
            StringComparison.Ordinal));
    }

    [Fact]
    public void TheReadmeSaysEachPermitIsClaimedByItsInterlock()
    {
        string readme = Readme;

        Assert.Contains("each interlock claims its device's permit", readme, StringComparison.Ordinal);
        Assert.Contains("Tag 'CV001.Permit' is claimed by INT_CV001; a scenario cannot write it.", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("ordinary, writable tags", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("The permit is not protected", readme, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryQuotedLineIsAWholeLineOfItsGolden(string name)
    {
        Match block = Assert.Single(QuotedBlock().Matches(Readme), m => m.Groups[1].Value == $"expected/{name}.log");
        HashSet<string> golden = [.. File.ReadAllText(Sample.Golden(name)).ReplaceLineEndings("\n").Split('\n')];
        string[] lines = block.Groups[2].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.True(golden.Contains(line), $"README quotes a line '{name}' does not log: '{line}'."));
    }
}
