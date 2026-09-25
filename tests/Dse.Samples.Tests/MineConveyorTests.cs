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
    public void TheScenarioFolderHoldsExactlyTheEightScenariosEachWithAGoldenAndAStory()
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
