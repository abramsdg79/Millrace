using System.Globalization;
using System.Text.Json;
using Dse.Cli;
using Dse.Components.Flow;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Logging;
using Dse.Scenarios;
using Json.Schema;

namespace Dse.Samples.Tests;

/// <summary>
/// The wheel-line sample, end to end: the plant validates and the schema
/// accepts it; every scenario opens with the line start, matches its golden
/// through <c>dse run --expect</c>, tells its story, settles before it ends,
/// conserves mass and replays byte for byte from a recording; and the stories
/// the log cannot tell — a held billet's temperature, the belt's count, which
/// billet became which wheel — hold on a live run.
/// </summary>
public class WheelLineTests
{
    [Fact]
    public void ThePlantValidatesWithFiveControllers()
    {
        CliRun run = Cli.Run("validate", WheelLine.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.StartsWith($"OK  {WheelLine.Plant}\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  components    9\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  flow links    6\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  tags          39 (3 explicit)\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  controllers   5\n", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLoaderReportsNothingAtAllNotEvenAWarning()
    {
        LoadResult load = PlantLoader.Load(File.ReadAllText(WheelLine.Plant), Sample.Catalogue);

        Assert.True(load.IsValid, load.ToText());
        Assert.Empty(load.Diagnostics);
    }

    [Fact]
    public void TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt()
    {
        JsonSchema schema = JsonSchema.FromText(PlantSchema.Generate(Sample.Catalogue));
        string plant = File.ReadAllText(WheelLine.Plant);
        string broken = plant.Replace("\"dwellSeconds\": 1 }", "\"dwellSeconds\": \"1\" }", StringComparison.Ordinal);

        Assert.NotEqual(plant, broken);
        Assert.True(schema.Evaluate(JsonDocument.Parse(plant).RootElement).IsValid);
        Assert.False(schema.Evaluate(JsonDocument.Parse(broken).RootElement).IsValid);
    }

    [Fact]
    public void TheRejectGateIsClaimedByTheCoilAndTheBilletSourceByTheBayInterlock()
    {
        CliRun run = Cli.Run("tags", WheelLine.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        string[] claimed = run.Out.Split('\n').Where(l => l.Contains("  claimed by ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            [
                "Billets.Enabled  Bool  ReadOnly  Minting enabled  claimed by INT_BAY",
                "GATE.Reject  Bool  ReadOnly  Divert the item leaving now to the reject outlet  claimed by COIL_REJECT",
            ],
            claimed);
        Assert.Contains("CV.ItemCount  Int64  ReadOnly  count  Blanks on the belt\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("FCE.ZONE_SP  Double  ReadWrite  °C  [0, 1400]  Furnace zone temperature setpoint\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("CV.SPEED_SP  Double  ReadWrite  m/s  [0, 1]  Belt speed setpoint\n", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GATE.Reject", "COIL_REJECT")]
    [InlineData("Billets.Enabled", "INT_BAY")]
    public void AScenarioThatWritesAClaimedTagIsRefusedBeforeTickZero(string tag, string block)
    {
        Scenario scenario = ScenarioLoader.Parse(
            $$"""{ "plant": "../plant.json", "duration": 60, "timeline": [ { "at": 10, "write": "{{tag}}", "value": true } ] }""").Scenario!;

        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(WheelLine.Plant), Sample.Catalogue);

        ConfigDiagnostic d = Assert.Single(result.Diagnostics);
        Assert.Equal(("DSE206", "$.timeline[0].write"), (d.Code, d.Path));
        Assert.Equal($"Tag '{tag}' is claimed by {block}; a scenario cannot write it.", d.Message);
        Assert.Null(result.Events);
    }

    [Fact]
    public void TheScenarioFolderHoldsExactlyTheSixScenariosEachWithAGoldenAndAStory()
    {
        string[] onDisk = Directory.GetFiles(Path.Combine(WheelLine.SourceRoot, "scenarios"), "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected = WheelLine.Names.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, onDisk);
        Assert.Equal(expected, WheelLineStories.All.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(WheelLine.Names, name => Assert.True(File.Exists(WheelLine.SourceGolden(name)), $"'{name}' has no golden."));
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioOpensWithTheLineStartAndTheCoilsFirstScan(string name)
    {
        Scenario scenario = ScenarioLoader.Parse(File.ReadAllText(WheelLine.Scenario(name))).Scenario!;
        IReadOnlyList<SimEventRecord> events = WheelLine.Run(name).Events!.Records;

        WriteAction zone = Assert.IsType<WriteAction>(scenario.Timeline[0]);
        WriteAction belt = Assert.IsType<WriteAction>(scenario.Timeline[1]);
        Assert.Equal((TimeSpan.Zero, "FCE.ZONE_SP"), (zone.At, zone.Tag));
        Assert.Equal((TimeSpan.Zero, "CV.SPEED_SP"), (belt.At, belt.Tag));
        Assert.Equal(
            [
                "06:00:00.000  FCE.ZONE_SP  WRITE  Set to 1250.",
                "06:00:00.000  CV.SPEED_SP  WRITE  Set to 0.5.",
                "06:00:00.100  GATE.Reject  WRITE  Set to false by COIL_REJECT.",
            ],
            events.Take(3).Select(CausalChain.Line));
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioMatchesItsGolden(string name)
    {
        if (Sample.Updating)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WheelLine.SourceGolden(name))!);
            CliRun made = Cli.Run("run", WheelLine.Scenario(name), "--out", WheelLine.SourceGolden(name));
            Assert.Equal(ExitCodes.Ok, made.ExitCode);
            return;
        }

        string golden = WheelLine.Golden(name);

        CliRun run = Cli.Run("run", WheelLine.Scenario(name), "--expect", golden);

        Assert.True(run.ExitCode == ExitCodes.Ok, run.Err);
        Assert.Empty(run.Err);
        Assert.StartsWith($"Matched {golden} (", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioTellsItsStory(string name)
    {
        IReadOnlyList<SimEventRecord> events = WheelLine.Run(name).Events!.Records;
        Story story = WheelLineStories.All[name];

        Assert.Null(CausalChain.FindChain(events, story.Chain));
        Assert.All(story.Absences, absence => Assert.Null(CausalChain.FindAbsence(events, absence)));
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioSettlesAtLeastFiveHundredTicksBeforeItEnds(string name)
    {
        ScenarioRunResult result = WheelLine.Run(name);

        long quietTicks = result.Summary!.Ticks - result.Events!.Records[^1].Tick;

        Assert.True(
            quietTicks >= 500,
            $"'{name}' logs its last event {quietTicks} ticks before the end; lengthen its duration so the consequence settles.");
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioConservesMassOnEveryTick(string name)
    {
        double worst = 0.0;

        Simulation end = WheelLine.Watch(name, sim => worst = Math.Max(worst, Math.Abs(sim.MassBalance.Drift)));

        MassBalance balance = end.MassBalance;
        ItemSink wheels = WheelLine.Component<ItemSink>(end, "Wheels");
        ItemSink bay = WheelLine.Component<ItemSink>(end, "Bay");
        Assert.True(worst <= 1e-9, string.Create(CultureInfo.InvariantCulture, $"'{name}' drifts {worst} kg."));
        Assert.True(balance.Created > 0.0, $"'{name}' makes no billet.");

        // Every billet made is a wheel (92 % of it: the press's yield; the rest is flash), a reject,
        // or still in the line.
        Assert.Equal(balance.Created, (wheels.MassReceived / 0.92) + bay.MassReceived + balance.Held, 6);
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioReplaysByteForByteFromARecording(string name)
    {
        string path = WheelLine.Scenario(name);
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
