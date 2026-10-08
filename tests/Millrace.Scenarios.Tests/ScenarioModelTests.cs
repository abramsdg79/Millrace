using Millrace.Configuration;
using Millrace.Core.Faults;
using Millrace.Io;

namespace Millrace.Scenarios.Tests;

public class ScenarioModelTests
{
    private static Scenario Minimal(params ScenarioAction[] timeline) => new(
        "conveyor-line.json", 42UL, new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(120), timeline);

    [Fact]
    public void ThePlantPathIsResolvedBesideTheScenarioFile()
    {
        Scenario scenario = Minimal();

        string resolved = scenario.ResolvePlantPath(Path.Combine("/plants", "line", "run.json"));

        Assert.Equal(Path.GetFullPath(Path.Combine("/plants", "line", "conveyor-line.json")), resolved);
    }

    [Fact]
    public void ARelativePlantPathMayClimbOut()
    {
        var scenario = Minimal() with { PlantPath = Path.Combine("..", "shared", "plant.json") };

        string resolved = scenario.ResolvePlantPath(Path.Combine("/plants", "line", "run.json"));

        Assert.Equal(Path.GetFullPath(Path.Combine("/plants", "shared", "plant.json")), resolved);
    }

    [Fact]
    public void AnAbsolutePlantPathIsUsedAsItStands()
    {
        string absolute = Path.GetFullPath(Path.Combine("/elsewhere", "plant.json"));
        var scenario = Minimal() with { PlantPath = absolute };

        Assert.Equal(absolute, scenario.ResolvePlantPath(Path.Combine("/plants", "run.json")));
    }

    [Fact]
    public void TheOverridesBecomeLoadOptions()
    {
        LoadOptions options = Minimal().ToLoadOptions();

        Assert.Equal(42UL, options.Seed);
        Assert.Equal(TimeSpan.FromMilliseconds(10), options.TimeStep);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), options.StartTime);
    }

    [Fact]
    public void AnAbsentOverrideStaysAbsentSoThePlantDecides()
    {
        var scenario = new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(1), []);

        LoadOptions options = scenario.ToLoadOptions();

        Assert.Null(options.Seed);
        Assert.Null(options.TimeStep);
        Assert.Null(options.StartTime);
    }

    [Theory]
    [InlineData(TagKind.Bool, true)]
    [InlineData(TagKind.Double, false)]
    [InlineData(TagKind.Int64, false)]
    public void ABoolValueFitsOnlyABoolTag(TagKind kind, bool fits)
    {
        Assert.Equal(fits, ScenarioValue.OfBool(true).ToTagValue(kind) is not null);
    }

    [Theory]
    [InlineData(TagKind.Bool, false)]
    [InlineData(TagKind.Double, true)]
    [InlineData(TagKind.Int64, false)]
    public void AFractionalNumberFitsOnlyADoubleTag(TagKind kind, bool fits)
    {
        Assert.Equal(fits, ScenarioValue.OfNumber(1.5).ToTagValue(kind) is not null);
    }

    [Theory]
    [InlineData(TagKind.Bool, false)]
    [InlineData(TagKind.Double, true)]
    [InlineData(TagKind.Int64, true)]
    public void AWholeNumberFitsEitherNumericTag(TagKind kind, bool fits)
    {
        Assert.Equal(fits, ScenarioValue.OfInteger(7L).ToTagValue(kind) is not null);
    }

    [Fact]
    public void AValueCarriesItsPayloadThrough()
    {
        Assert.True(ScenarioValue.OfBool(true).ToTagValue(TagKind.Bool)!.Value.AsBool);
        Assert.Equal(1.5, ScenarioValue.OfNumber(1.5).ToTagValue(TagKind.Double)!.Value.AsDouble);
        Assert.Equal(7.0, ScenarioValue.OfInteger(7L).ToTagValue(TagKind.Double)!.Value.AsDouble);
        Assert.Equal(7L, ScenarioValue.OfInteger(7L).ToTagValue(TagKind.Int64)!.Value.AsInt64);
    }

    [Fact]
    public void AValuePrintsAsTheFileWouldWriteIt()
    {
        Assert.Equal("true", ScenarioValue.OfBool(true).ToString());
        Assert.Equal("false", ScenarioValue.OfBool(false).ToString());
        Assert.Equal("1.5", ScenarioValue.OfNumber(1.5).ToString());
        Assert.Equal("7", ScenarioValue.OfInteger(7L).ToString());
    }

    [Fact]
    public void AnActionKnowsItsOwnTimeWhateverItsShape()
    {
        ScenarioAction[] actions =
        [
            new WriteAction(TimeSpan.FromSeconds(5), "CV001.Start", ScenarioValue.OfBool(true)),
            new FaultAction(TimeSpan.FromSeconds(30), "CV001.Motor", "thermal-bias", [new FaultArgument("amount", 0.8)]),
            new ClearAction(TimeSpan.FromSeconds(60.5), "CV001.Motor", "thermal-bias"),
        ];

        Assert.Equal(
            new[] { 5.0, 30.0, 60.5 },
            actions.Select(a => a.At.TotalSeconds));
    }
}
