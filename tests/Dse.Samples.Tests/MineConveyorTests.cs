using System.Globalization;
using System.Text.Json;
using Dse.Cli;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Logging;
using Dse.Scenarios;
using Json.Schema;

namespace Dse.Samples.Tests;

/// <summary>
/// The mine-conveyor sample, end to end: the plant validates and the schema
/// accepts it; every scenario matches its golden through <c>dse run --expect</c>,
/// tells its story, settles before it ends, and replays byte for byte from a
/// recording.
/// </summary>
public class MineConveyorTests
{
    [Fact]
    public void ThePlantValidatesWithTwelveControllers()
    {
        CliRun run = Cli.Run("validate", Sample.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.StartsWith($"OK  {Sample.Plant}\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  controllers   12\n", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLoaderReportsNothingAtAllNotEvenAWarning()
    {
        LoadResult load = PlantLoader.Load(File.ReadAllText(Sample.Plant), Sample.Catalogue);

        Assert.True(load.IsValid, load.ToText());
        Assert.Empty(load.Diagnostics);
    }

    [Fact]
    public void TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt()
    {
        JsonSchema schema = JsonSchema.FromText(PlantSchema.Generate(Sample.Catalogue));
        string plant = File.ReadAllText(Sample.Plant);
        string broken = plant.Replace("\"lengthM\": 60", "\"lengthM\": \"60\"", StringComparison.Ordinal);

        Assert.NotEqual(plant, broken);
        Assert.True(schema.Evaluate(JsonDocument.Parse(plant).RootElement).IsValid);
        Assert.False(schema.Evaluate(JsonDocument.Parse(broken).RootElement).IsValid);
    }

    [Fact]
    public void TheScenarioFolderHoldsExactlyTheNamedScenariosEachWithAGoldenAndAStory()
    {
        string[] onDisk = Directory.GetFiles(Path.Combine(Sample.SourceRoot, "scenarios"), "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected = Sample.Names.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, onDisk);
        Assert.Equal(expected, Stories.All.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(Sample.Names, name => Assert.True(File.Exists(Sample.SourceGolden(name)), $"'{name}' has no golden."));
    }

    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryScenarioMatchesItsGolden(string name)
    {
        if (Sample.Updating)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Sample.SourceGolden(name))!);
            CliRun made = Cli.Run("run", Sample.Scenario(name), "--out", Sample.SourceGolden(name));
            Assert.Equal(ExitCodes.Ok, made.ExitCode);
            return;
        }

        string golden = Sample.Golden(name);

        CliRun run = Cli.Run("run", Sample.Scenario(name), "--expect", golden);

        Assert.True(run.ExitCode == ExitCodes.Ok, run.Err);
        Assert.Empty(run.Err);
        Assert.StartsWith($"Matched {golden} (", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryScenarioTellsItsStory(string name)
    {
        IReadOnlyList<SimEventRecord> events = Sample.Run(name).Events!.Records;
        Story story = Stories.All[name];

        Assert.Null(CausalChain.FindChain(events, story.Chain));
        Assert.All(story.Absences, absence => Assert.Null(CausalChain.FindAbsence(events, absence)));
    }

    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryScenarioSettlesAtLeastFiveSecondsBeforeItEnds(string name)
    {
        ScenarioRunResult result = Sample.Run(name);

        long quietTicks = result.Summary!.Ticks - result.Events!.Records[^1].Tick;

        Assert.True(
            quietTicks >= 500,
            $"'{name}' logs its last event {quietTicks} ticks before the end; lengthen its duration so the consequence settles.");
    }

    [Fact]
    public void AStarvedFeedEmptiesTheBeltsInTransportOrder()
    {
        string[] scales = ["CV001.TonnesPerHour", "CV002.TonnesPerHour", "CV003.TonnesPerHour"];

        IReadOnlyDictionary<string, IReadOnlyList<TagSample>> traces =
            Sample.Trace("feed-starve", scales, TimeSpan.FromMilliseconds(100));

        Assert.Null(StateChain.FindFallInOrder(traces, scales, TimeSpan.FromSeconds(80), floor: 200, ceiling: 5));
    }

    [Fact]
    public void AWeldedContactorKeepsCV003AtSpeedThroughTheStopAndTheEStop()
    {
        IReadOnlyDictionary<string, IReadOnlyList<TagSample>> traces =
            Sample.Trace("welded-contactor", ["CV003.Speed"], TimeSpan.FromMilliseconds(100));
        IReadOnlyList<TagSample> series = traces["CV003.Speed"].Where(s => s.Time >= TimeSpan.FromSeconds(80)).ToList();

        // The trace's last sample lands at 00:04:00 (240.000 s, the scenario's full
        // duration); guard against a filter or a shortened trace silently emptying
        // this series and making Assert.All pass on nothing.
        Assert.Contains(series, s => s.Time >= TimeSpan.FromSeconds(239));

        Assert.All(
            series,
            s => Assert.True(s.Value >= 1.7, string.Create(CultureInfo.InvariantCulture, $"CV003 slowed to {s.Value} m/s at {s.Time}.")));
    }

    [Fact]
    public void AStartWrittenWhileTrippedMovesNothingAndTheResetDoesNotReleaseIt()
    {
        IReadOnlyDictionary<string, IReadOnlyList<TagSample>> traces = Sample.Trace(
            "start-while-tripped", ["CV001.Speed", "CV001.TonnesPerHour", "Feed.HopperMass"], TimeSpan.FromMilliseconds(100));
        List<TagSample> Between(string tag, double from, double to) =>
            traces[tag].Where(s => s.Time >= TimeSpan.FromSeconds(from) && s.Time < TimeSpan.FromSeconds(to)).ToList();

        // The trace's last sample lands at 00:02:30 (150.000 s, the full duration);
        // guard against an empty series making Assert.All pass on nothing.
        Assert.Contains(traces["CV001.Speed"], s => s.Time >= TimeSpan.FromSeconds(149));

        // (a) and (b): from the refused start at 100 s, through the interlock's reset at 115 s,
        // to the operator's fresh start at 125 s, CV001 does not move.
        Assert.All(Between("CV001.Speed", 100, 125), s => Assert.True(
            s.Value <= 0.02, string.Create(CultureInfo.InvariantCulture, $"CV001 moved at {s.Value} m/s at {s.Time}.")));
        Assert.All(Between("CV001.TonnesPerHour", 100, 125), s => Assert.True(
            s.Value <= 5.0, string.Create(CultureInfo.InvariantCulture, $"CV001 carried {s.Value} t/h at {s.Time}.")));

        // The feeder makes nothing from 100 s to the end: not while tripped, not after its reset at 130 s.
        Assert.All(Between("Feed.HopperMass", 100, 151), s => Assert.True(
            s.Value == 0.0, string.Create(CultureInfo.InvariantCulture, $"The feeder made {s.Value} kg by {s.Time}.")));

        // (c): the fresh start at 125 s works.
        Assert.True(traces["CV001.Speed"][^1].Value >= 1.74, string.Create(
            CultureInfo.InvariantCulture, $"CV001 ended at {traces["CV001.Speed"][^1].Value} m/s, not running."));
    }

    [Fact]
    public void EveryPermitIsReadOnlyAndClaimedByItsInterlock()
    {
        CliRun run = Cli.Run("tags", Sample.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        string[] claimed = run.Out.Split('\n').Where(l => l.Contains("  claimed by ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            [
                "CV001.Permit  Bool  ReadOnly  Run permit; false holds the contactor open  claimed by INT_CV001",
                "CV002.Permit  Bool  ReadOnly  Run permit; false holds the contactor open  claimed by INT_CV002",
                "CV003.Permit  Bool  ReadOnly  Run permit; false holds the contactor open  claimed by INT_CV003",
                "Feed.Permit  Bool  ReadOnly  Run permit; false stops the feeder  claimed by INT_FEED",
            ],
            claimed);
    }

    [Theory]
    [InlineData("CV001.Permit", "INT_CV001")]
    [InlineData("Feed.Permit", "INT_FEED")]
    public void AScenarioThatWritesAPermitIsRefusedBeforeTickZero(string permit, string interlock)
    {
        Scenario scenario = ScenarioLoader.Parse(
            $$"""{ "plant": "../plant.json", "duration": 150, "timeline": [ { "at": 100, "write": "{{permit}}", "value": true } ] }""").Scenario!;

        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(Sample.Plant), Sample.Catalogue);

        ConfigDiagnostic d = Assert.Single(result.Diagnostics);
        Assert.Equal(("DSE206", "$.timeline[0].write"), (d.Code, d.Path));
        Assert.Equal($"Tag '{permit}' is claimed by {interlock}; a scenario cannot write it.", d.Message);
        Assert.Null(result.Events);
    }

    [Fact]
    public void ATraceCannotSampleFasterThanTheTimeStep()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Sample.Trace("feed-starve", ["CV001.Speed"], TimeSpan.FromMilliseconds(5)));
    }

    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryScenarioReplaysByteForByteFromARecording(string name)
    {
        string path = Sample.Scenario(name);
        Scenario scenario = ScenarioLoader.Parse(File.ReadAllText(path)).Scenario!;
        string plantJson = File.ReadAllText(scenario.ResolvePlantPath(path));
        LoadResult load = PlantLoader.Load(plantJson, Sample.Catalogue, scenario.ToLoadOptions());
        Assert.True(load.IsValid, load.ToText());

        Simulation live = load.Builder!.Build();
        var recorder = new ScenarioRecorder();
        live.AttachActionRecorder(recorder);
        foreach (ScenarioAction action in scenario.Timeline)
        {
            Sample.Schedule(live, action);
        }

        live.RunFor(scenario.Duration);

        Assert.Equal(scenario.Timeline.Count, recorder.Count);
        Scenario recorded = recorder.ToScenario("../plant.json", load.Options!, scenario.Duration);
        ScenarioParseResult reparsed = ScenarioLoader.Parse(ScenarioJson.Write(recorded));
        Assert.Empty(reparsed.Diagnostics);

        ScenarioRunResult replay = ScenarioRunner.Run(reparsed.Scenario!, plantJson, Sample.Catalogue);

        Assert.True(replay.IsValid, replay.ToText());
        Assert.Equal(live.Events.ToText(), replay.Events!.ToText());
    }
}
