using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Millrace.Samples.Tests;

/// <summary>
/// The wheel-line README quotes each scenario's log in a block fenced as
/// <c>```text expected/&lt;name&gt;.log</c>; every quoted line must be a whole
/// line of that golden. Its dwell arithmetic must be this plant's, and the
/// repository's own pages must point at the sample.
/// </summary>
public partial class WheelLineReadmeTests
{
    [GeneratedRegex(@"^```text (expected/[a-z-]+\.log)\n(.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex QuotedBlock();

    private static string Readme => File.ReadAllText(WheelLine.Readme).ReplaceLineEndings("\n");

    [Fact]
    public void EveryScenarioHasOneQuotedBlockAndItsCommand()
    {
        string readme = Readme;
        string[] quoted = QuotedBlock().Matches(readme).Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(WheelLine.Names.Select(n => $"expected/{n}.log"), quoted);
        Assert.All(WheelLine.Names, name => Assert.Contains(
            $"run samples/wheel-line/scenarios/{name}.json --expect samples/wheel-line/expected/{name}.log",
            readme,
            StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryQuotedLineIsAWholeLineOfItsGolden(string name)
    {
        Match block = Assert.Single(QuotedBlock().Matches(Readme), m => m.Groups[1].Value == $"expected/{name}.log");
        HashSet<string> golden = [.. File.ReadAllText(WheelLine.Golden(name)).ReplaceLineEndings("\n").Split('\n')];
        string[] lines = block.Groups[2].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.True(golden.Contains(line), $"README quotes a line '{name}' does not log: '{line}'."));
    }

    [Fact]
    public void TheDwellArithmeticIsThisPlantsAndTheLineStartIsEveryGoldensFirstThreeLines()
    {
        string readme = Readme;
        using JsonDocument plant = JsonDocument.Parse(File.ReadAllText(WheelLine.Plant));
        JsonElement root = plant.RootElement;
        JsonElement gate = root.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "GATE");
        JsonElement pyro = root.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "PYRO");
        JsonElement[] controllers = root.GetProperty("controllers").EnumerateArray().ToArray();
        JsonElement hiHi = controllers.Single(c => c.GetProperty("id").GetString() == "ALM_PYRO")
            .GetProperty("parameters").GetProperty("limits").EnumerateArray()
            .Single(l => l.GetProperty("kind").GetString() == "hi-hi");

        // The README's numbers: dt 0.1 s, both blocks every 100 ms, a 1 s dwell, no on-delay, no lag.
        Assert.Equal(100, root.GetProperty("defaults").GetProperty("timeStepMs").GetInt32());
        Assert.Equal(1.0, gate.GetProperty("parameters").GetProperty("dwellSeconds").GetDouble());
        Assert.Equal(0.0, pyro.GetProperty("parameters").GetProperty("spec").GetProperty("lagSeconds").GetDouble());
        Assert.False(hiHi.TryGetProperty("onDelayS", out _));
        Assert.All(controllers, c => Assert.Equal(100, c.GetProperty("scanPeriodMs").GetInt32()));

        // The coil follows HiHi, not Hi (the two raise together, R179), and drives the kicker.
        JsonElement coil = controllers.Single(c => c.GetProperty("id").GetString() == "COIL_REJECT").GetProperty("parameters");
        Assert.Equal("ALM_PYRO.HiHi.Active", coil.GetProperty("condition").GetProperty("tag").GetString());
        Assert.Equal("GATE.Reject", coil.GetProperty("output").GetString());
        Assert.Contains("⌈dwell / dt⌉ ≥ a + c + 1 + a·⌈onDelay / (a·dt)⌉, plus the pyrometer's lag.", readme, StringComparison.Ordinal);
        Assert.Contains("⌈1.0 s / 0.1 s⌉ = 10 ≥ 1 + 1 + 1 + 0 = 3", readme, StringComparison.Ordinal);

        const string LineStart =
            "06:00:00.000  FCE.ZONE_SP  WRITE  Set to 1250.\n" +
            "06:00:00.000  CV.SPEED_SP  WRITE  Set to 0.5.\n" +
            "06:00:00.100  GATE.Reject  WRITE  Set to false by COIL_REJECT.\n";
        Assert.Contains("```\n" + LineStart + "```\n", readme, StringComparison.Ordinal);
        Assert.All(WheelLine.Names, name => Assert.StartsWith(
            LineStart, File.ReadAllText(WheelLine.Golden(name)).ReplaceLineEndings("\n"), StringComparison.Ordinal));
    }

    [Fact]
    public void TheReadmeListsTheKnownLimitsAndWhyTheQueueAlarmWatchesADetector()
    {
        string readme = Readme;

        foreach (string token in new[]
                 {
                     "**No burner or zone controller.**", "**No descaler.**", "**One press.**",
                     "**Lumped billet temperature.**", "**The zone held as a written setpoint.**",
                     "a belt's `ItemCount` is an Int64", "(`MR114`)", "claimed by COIL_REJECT",
                     "the full bay and `INT_BAY` are shown by scenario 6",
                 })
        {
            Assert.Contains(token, readme, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRootReadmeTheControlBlocksPageAndTheMainSpecNameTheWheelLine()
    {
        string readme = RepositoryFile("README.md");
        string controlBlocks = RepositoryFile("docs", "control-blocks.md");
        string mainSpec = RepositoryFile("docs", "superpowers", "specs", "2026-09-02-industrial-process-simulation-engine-design.md");

        Assert.Contains("`samples/wheel-line/`, is a forging cell of discrete items", readme, StringComparison.Ordinal);
        Assert.Contains("[wheel-line sample](samples/wheel-line/README.md)", readme, StringComparison.Ordinal);
        Assert.Contains("run samples/wheel-line/scenarios/slow-press.json", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("is planned (plan 6b.2)", readme, StringComparison.Ordinal);
        Assert.Contains("`samples/wheel-line/` puts the coil to work", controlBlocks, StringComparison.Ordinal);
        Assert.Contains("The wheel line is implemented in `samples/wheel-line/` (plan 6b.2)", mainSpec, StringComparison.Ordinal);
        Assert.DoesNotContain("The wheel line is plan 6b. Its chain needs", mainSpec, StringComparison.Ordinal);
    }

    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string RepositoryFile(string first, params string[] rest) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), first, .. rest])).ReplaceLineEndings("\n");

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
