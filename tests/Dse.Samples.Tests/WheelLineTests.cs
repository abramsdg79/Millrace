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
    /// <summary>ALM_PYRO's HiHi limit: the over-soak the coil rejects on, °C.</summary>
    private const double HiHi = 1150.0;

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

    [Fact]
    public void TheRejectDecisionLandsSevenTicksBeforeTheOverSoakedBilletLeaves()
    {
        IReadOnlyList<SimEventRecord> events = WheelLine.Run("slow-press").Events!.Records;
        int rejected = events.ToList().FindIndex(r => r.Source == "GATE" && r.Code == "REJECTED");
        Assert.True(rejected >= 0, "slow-press rejects nothing.");

        // The billet lands on the gate on the tick the furnace logs IDLE (tick N).
        long n = events.Take(rejected).Last(r => r.Source == "FCE" && r.Code == "IDLE").Tick;
        long hiHi = events.Take(rejected).Last(r => r.Source == "ALM_PYRO" && r.Message.StartsWith("HiHi: ", StringComparison.Ordinal)).Tick;
        long write = events.Take(rejected).Last(r => r.Source == "GATE.Reject" && r.Message == "Set to true by COIL_REJECT.").Tick;

        // R168 with a = c = 1, no on-delay and no lag: the pyrometer reads at N + 1, the alarm
        // raises at N + 2, the coil's write lands at N + a + c + 2 = N + 4; the billet may leave at
        // N + ceil(1.0 s / 0.1 s) + 1 = N + 11.
        Assert.Equal((n + 2, n + 4, n + 11), (hiHi, write, events[rejected].Tick));
    }

    [Fact]
    public void ASlowPressOverSoaksTheHeldBilletOnlyWhileItIsSlowAndTheBeltFillsAndDrains()
    {
        var hottest = new List<(TimeSpan Time, double Celsius)>();
        var count = new List<(TimeSpan Time, long Blanks)>();
        var discharging = new List<(TimeSpan Time, bool Held)>();
        ItemProcessUnit? furnace = null;
        RejectGate? gate = null;

        WheelLine.Watch("slow-press", sim =>
        {
            furnace ??= WheelLine.Component<ItemProcessUnit>(sim, "FCE");
            gate ??= WheelLine.Component<RejectGate>(sim, "GATE");
            TimeSpan now = sim.Clock.Elapsed;
            double inFurnace = furnace.Items.Count > 0 ? furnace.Items.Max(i => i.Properties.Temperature) : 0.0;
            hottest.Add((now, Math.Max(inFurnace, gate.Item?.Properties.Temperature ?? 0.0)));
            count.Add((now, sim.IO.Read("CV.ItemCount").AsInt64));
            discharging.Add((now, sim.IO.Read("FCE.Phase").AsInt64 == (long)ProcessPhase.Discharging));
        });

        TimeSpan slow = TimeSpan.FromSeconds(300);
        TimeSpan cleared = TimeSpan.FromSeconds(1140);

        // Steady before the fault: one blank at most on the belt, every billet at its 1100 °C target.
        Assert.All(count.Where(s => s.Time <= slow), s => Assert.True(s.Blanks <= 1, $"{s.Blanks} blanks at {s.Time}."));
        Assert.All(hottest.Where(s => s.Time <= slow), s => Assert.True(s.Celsius < 1101.0, $"{s.Celsius} °C at {s.Time}."));

        // Slow: the belt fills (six blanks at 2 m spacing on 10 m), the furnace holds a billet in
        // Discharging for minutes (measured 285.7 s), and that billet soaks toward its 1250 °C zone
        // (measured 1249.999 °C) — well above HiHi.
        Assert.Contains(count, s => s.Blanks == 6 && s.Time > slow && s.Time < cleared);
        Assert.True(LongestRun(discharging) >= TimeSpan.FromSeconds(280), $"The furnace held a billet for only {LongestRun(discharging)}.");
        Assert.InRange(hottest.Max(s => s.Celsius), 1245.0, 1250.0);

        // Past HiHi only while the press is slow (measured 840.2 s to 1117.1 s).
        Assert.All(hottest.Where(s => s.Celsius > HiHi), s => Assert.InRange(s.Time, slow, cleared));

        // After the clear the queue drains: from 2000 s on (measured 1981.5 s) one blank at most.
        Assert.All(count.Where(s => s.Time >= TimeSpan.FromSeconds(2000)), s => Assert.True(s.Blanks <= 1, $"{s.Blanks} blanks at {s.Time}."));
    }

    [Fact]
    public void AStuckKickerTurnsTheOverSoakedBilletsIntoWheels()
    {
        var overSoaked = new SortedSet<long>();
        var wheels = new SortedDictionary<long, string>();
        RejectGate? gate = null;
        ItemSink? sink = null;

        Simulation end = WheelLine.Watch("stuck-kicker", sim =>
        {
            gate ??= WheelLine.Component<RejectGate>(sim, "GATE");
            sink ??= WheelLine.Component<ItemSink>(sim, "Wheels");
            if (gate.Item is { } onGate && onGate.Properties.Temperature > HiHi)
            {
                overSoaked.Add(onGate.Id);
            }

            if (sink.LastItem is { } wheel)
            {
                wheels[wheel.Id] = wheel.Type.Name;
            }
        });

        // Billet 13 soaked to 1250 °C behind the slow press; billet 14 (1169.5 °C) was held behind it
        // while the stuck kicker kept 13 on the gate. Both became wheels; nothing reached the bay.
        Assert.Equal([13L, 14L], overSoaked);
        Assert.All(overSoaked, id => Assert.Equal("Wheel", Assert.Contains(id, (IDictionary<long, string>)wheels)));
        Assert.Equal(0L, WheelLine.Component<ItemSink>(end, "Bay").Count.Value);
        Assert.Equal(0L, end.IO.Read("GATE.Rejected").AsInt64);
    }

    /// <summary>The longest stretch of consecutive samples that are true.</summary>
    private static TimeSpan LongestRun(List<(TimeSpan Time, bool Held)> samples)
    {
        TimeSpan longest = TimeSpan.Zero;
        TimeSpan? since = null;
        TimeSpan previous = TimeSpan.Zero;
        foreach ((TimeSpan time, bool held) in samples)
        {
            since = held ? since ?? previous : null;
            if (since is { } start && time - start > longest)
            {
                longest = time - start;
            }

            previous = time;
        }

        return longest;
    }
}
