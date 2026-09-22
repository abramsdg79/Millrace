using Dse.Core.Faults;
using Dse.Core.Time;
using Dse.Io;

namespace Dse.Scenarios.Tests;

public class ScenarioRecorderTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 9UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void AnEmptyRecordingIsAnEmptyTimeline()
    {
        Scenario scenario = new ScenarioRecorder().ToScenario("p.json", Options, TimeSpan.FromSeconds(1));

        Assert.Empty(scenario.Timeline);
        Assert.Equal(0, new ScenarioRecorder().Count);
    }

    [Fact]
    public void TheOverridesAreTheRunsActualOptions()
    {
        Scenario scenario = new ScenarioRecorder().ToScenario("p.json", Options, TimeSpan.FromSeconds(120));

        Assert.Equal("p.json", scenario.PlantPath);
        Assert.Equal(9UL, scenario.Seed);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), scenario.StartTime);
        Assert.Equal(TimeSpan.FromMilliseconds(10), scenario.TimeStep);
        Assert.Equal(TimeSpan.FromSeconds(120), scenario.Duration);
    }

    [Fact]
    public void EachActionIsTimedAtItsTickTimesTheStep()
    {
        var recorder = new ScenarioRecorder();
        recorder.Wrote(500L, "CV001.Start", TagValue.Bool(true));
        recorder.Faulted(3000L, "CV001.Motor", "thermal-bias", new FaultArguments(new FaultArgument("amount", 0.8)));
        recorder.Cleared(6050L, "CV001.Motor", "thermal-bias");

        Scenario scenario = recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(120));

        Assert.Equal(3, recorder.Count);
        Assert.Equal(
            new[] { 5.0, 30.0, 60.5 },
            scenario.Timeline.Select(a => a.At.TotalSeconds));
    }

    [Fact]
    public void EachShapeIsRecordedWithItsOwnFields()
    {
        var recorder = new ScenarioRecorder();
        recorder.Wrote(100L, "T.Enable", TagValue.Bool(true));
        recorder.Faulted(200L, "F", "blow", new FaultArguments(new FaultArgument("resistance", 2.5)));
        recorder.Cleared(300L, "F", "blow");

        Scenario scenario = recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(10));

        var write = Assert.IsType<WriteAction>(scenario.Timeline[0]);
        Assert.Equal(("T.Enable", ScenarioValue.OfBool(true)), (write.Tag, write.Value));
        var fault = Assert.IsType<FaultAction>(scenario.Timeline[1]);
        Assert.Equal(("F", "blow"), (fault.ComponentId, fault.FaultId));
        Assert.Equal(new[] { new FaultArgument("resistance", 2.5) }, fault.Arguments);
        var clear = Assert.IsType<ClearAction>(scenario.Timeline[2]);
        Assert.Equal(("F", "blow"), (clear.ComponentId, clear.FaultId));
    }

    [Fact]
    public void EachTagKindKeepsItsValueKind()
    {
        var recorder = new ScenarioRecorder();
        recorder.Wrote(0L, "B", TagValue.Bool(false));
        recorder.Wrote(0L, "D", TagValue.Double(1.5));
        recorder.Wrote(0L, "I", TagValue.Int64(7L));

        Scenario scenario = recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(1));

        Assert.Equal(
            new[] { ScenarioValueKind.Bool, ScenarioValueKind.Number, ScenarioValueKind.Integer },
            scenario.Timeline.Cast<WriteAction>().Select(w => w.Value.Kind));
    }

    [Fact]
    public void ARecordingIsWrittenAndParsedBack()
    {
        var recorder = new ScenarioRecorder();
        recorder.Wrote(500L, "CV001.Start", TagValue.Bool(true));
        recorder.Faulted(3000L, "CV001.Motor", "thermal-bias", new FaultArguments(new FaultArgument("amount", 0.8)));

        string json = ScenarioJson.Write(recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(120)));
        ScenarioParseResult parsed = ScenarioLoader.Parse(json);

        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(json, ScenarioJson.Write(parsed.Scenario!));
    }

    [Fact]
    public void ARecordingIsImmutableOnceTaken()
    {
        var recorder = new ScenarioRecorder();
        recorder.Wrote(0L, "T", TagValue.Bool(true));
        Scenario first = recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(1));

        recorder.Wrote(100L, "T", TagValue.Bool(false));

        Assert.Single(first.Timeline);
        Assert.Equal(2, recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(1)).Timeline.Count);
    }
}
