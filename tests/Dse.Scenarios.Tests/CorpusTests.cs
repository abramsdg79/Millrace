using Dse.Configuration;
using Dse.Tests.Shared;

namespace Dse.Scenarios.Tests;

public class CorpusTests
{
    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidScenarioParsesClean(string name)
    {
        ScenarioParseResult result = ScenarioLoader.Parse(Corpus.Text("valid", name));

        Assert.True(result.IsValid, result.ToText());
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Scenario);
    }

    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidScenarioNamesAPlantThatExists(string name)
    {
        ScenarioParseResult result = ScenarioLoader.Parse(Corpus.Text("valid", name));

        string plant = result.Scenario!.ResolvePlantPath(Corpus.PathOf("valid", name));

        Assert.True(File.Exists(plant), $"'{name}' names a plant at '{plant}', which is not there.");
    }

    /// <summary>
    /// One row per file under Scenarios/invalid: the file, the code it must
    /// yield, and the JSON path it must point at. A fixture that reported the
    /// right code at the wrong place would be a silent regression, so the path
    /// is pinned here rather than being merely "starts with $".
    /// </summary>
    public static IEnumerable<object[]> InvalidPaths() =>
    [
        ["DSE200-unterminated-timeline.json", "DSE200", "$"],
        ["DSE201-unknown-key.json", "DSE201", "$.seedd"],
        ["DSE202-duration-is-zero.json", "DSE202", "$.duration"],
        ["DSE203-off-tick-action.json", "DSE203", "$.timeline[0].at"],
        ["DSE204-two-shapes-in-one-action.json", "DSE204", "$.timeline[0]"],
    ];

    [Theory]
    [MemberData(nameof(InvalidPaths))]
    public void EveryInvalidScenarioYieldsExactlyTheCodeAndPathItsNameClaims(string name, string code, string path)
    {
        Assert.StartsWith(code, name, StringComparison.Ordinal);

        ScenarioParseResult result = ScenarioLoader.Parse(Corpus.Text("invalid", name));

        Assert.False(result.IsValid);
        Assert.Null(result.Scenario);
        ConfigDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, path), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void EveryInvalidFixtureHasARow()
    {
        var rows = InvalidPaths().Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);

        Assert.All(Corpus.Invalid(), file => Assert.Contains((string)file[0], rows));
        Assert.Equal(Corpus.Invalid().Count(), rows.Count);
    }

    [Theory]
    [MemberData(nameof(Corpus.Invalid), MemberType = typeof(Corpus))]
    public void EveryDiagnosticNamesItsFix(string name)
    {
        ScenarioParseResult result = ScenarioLoader.Parse(Corpus.Text("invalid", name));

        Assert.All(result.Diagnostics, d =>
        {
            Assert.EndsWith(".", d.Message, StringComparison.Ordinal);
            Assert.EndsWith(".", d.Fix, StringComparison.Ordinal);
            Assert.StartsWith("$", d.Path, StringComparison.Ordinal);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        });
    }

    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidScenarioRunsCleanAndMatchesItsGolden(string name)
    {
        (_, ScenarioRunResult? result) = Corpus.RunFile("valid", name);

        Assert.True(result!.IsValid, result.ToText());
        Assert.NotNull(result.Summary);
        Golden.Assert($"Golden/{Path.GetFileNameWithoutExtension(name)}.log", result.Events!.ToText());
    }

    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidScenarioIsByteIdenticalOnASecondRun(string name)
    {
        (_, ScenarioRunResult? first) = Corpus.RunFile("valid", name);
        (_, ScenarioRunResult? second) = Corpus.RunFile("valid", name);

        Assert.Equal(first!.Events!.ToText(), second!.Events!.ToText());
        Assert.Equal(first.Summary, second.Summary);
    }

    [Theory]
    [MemberData(nameof(Corpus.Unrunnable), MemberType = typeof(Corpus))]
    public void EveryUnrunnableScenarioLeadsWithTheCodeInItsName(string name)
    {
        string expected = name[..name.IndexOf('-', StringComparison.Ordinal)];

        (ScenarioParseResult parsed, ScenarioRunResult? result) = Corpus.RunFile("unrunnable", name);

        Assert.Empty(parsed.Diagnostics);
        Assert.False(result!.IsValid);
        Assert.Equal(expected, result.Diagnostics[0].Code);
        Assert.Null(result.Events);
    }

    [Fact]
    public void EveryScenarioCodeHasAFixture()
    {
        var covered = Corpus.Invalid()
            .Concat(Corpus.Unrunnable())
            .Select(row => ((string)row[0])[..6])
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(ScenarioDiagnostics.All, d => Assert.Contains(d.Code, covered));
    }
}
