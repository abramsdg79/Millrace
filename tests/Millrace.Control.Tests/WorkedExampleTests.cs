using Millrace.Components;
using Millrace.Configuration;
using Millrace.Control.Catalogue;
using Millrace.Core;
using Millrace.Core.Catalogue;
using Millrace.Core.Faults;
using Millrace.Io;
using Millrace.Tests.Shared;

namespace Millrace.Control.Tests;

/// <summary>
/// Spec 5c §5's worked example: the conveyor plant under a permissive, an
/// interlock, a current alarm and a start-up sequence, for two simulated
/// minutes. The example is built twice — with the blocks attached in code, as
/// in 5c, and from <c>conveyor-control.json</c>, which must match it byte for
/// byte.
/// </summary>
public class WorkedExampleTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(200);

    private static ComponentCatalogue Catalogue { get; } =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();

    private static BlockWrite Start(bool value) => new("CV001.Start", TagValue.Bool(value));

    private static Simulation Build()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Plants", "conveyor-line.json"));
        LoadResult result = PlantLoader.Load(json, Catalogue, new LoadOptions());
        Assert.True(result.IsValid, result.ToText());

        SimulationBuilder builder = result.Builder!;

        builder.AddScanBlock(new Permissive(
            "PERM01",
            [new Condition("CV001.SafetyOk", true), new Condition("Pile.Full", false)],
            Fast));

        builder.AddScanBlock(new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
            [Start(false)],
            Fast));

        builder.AddScanBlock(new Alarm(
            "CUR01",
            "CV001.Current",
            [
                new AlarmLimit(AlarmLimitKind.Hi, 3.0, 0.2, TimeSpan.FromSeconds(0.5)),
                // HiHi 8.0: measured start inrush peaks 11.88 A at +0.01 s, above 8 A for
                // 0.40 s and above 3 A for 1.37 s; running current is 1.38-1.56 A. The
                // thermal-bias overload at +40 s de-energises the motor, so the current
                // falls and both limits clear rather than re-raising.
                new AlarmLimit(AlarmLimitKind.HiHi, 8.0, 0.5, TimeSpan.FromSeconds(0.1)),
            ],
            Fast));

        builder.AddScanBlock(new Sequencer(
            "SEQ01",
            [
                new SequenceStep(
                    "Reset the safety relay",
                    [new BlockWrite("CV001.SafetyReset", TagValue.Bool(true))],
                    StepTransition.After(TimeSpan.FromSeconds(1))),
                new SequenceStep(
                    "Reset the interlock",
                    [
                        new BlockWrite("CV001.SafetyReset", TagValue.Bool(false)),
                        new BlockWrite("INT01.Reset", TagValue.Bool(true)),
                    ],
                    StepTransition.After(TimeSpan.FromSeconds(1))),
                new SequenceStep(
                    "Start the belt",
                    [new BlockWrite("INT01.Reset", TagValue.Bool(false)), Start(true)],
                    StepTransition.When("CV001.Speed", PredicateOperator.GreaterOrEqual, TagValue.Double(1.0)),
                    TimeSpan.FromSeconds(15)),
                new SequenceStep(
                    "Run the feed",
                    [new BlockWrite("Feed.Enabled", TagValue.Bool(true))],
                    StepTransition.After(TimeSpan.FromSeconds(60))),
                new SequenceStep(
                    "Stop the feed",
                    [new BlockWrite("Feed.Enabled", TagValue.Bool(false))],
                    StepTransition.After(TimeSpan.FromSeconds(2))),
                new SequenceStep(
                    "Stop the belt",
                    [Start(false)],
                    StepTransition.When("CV001.Stopped", PredicateOperator.Equal, TagValue.Bool(true)),
                    TimeSpan.FromSeconds(60)),
            ],
            Slow,
            [Start(false)]));

        Simulation sim = builder.Build();
        Drive(sim);
        return sim;
    }

    /// <summary>The operator's part of the example: start the sequence, release Start, inject the overload.</summary>
    private static void Drive(Simulation sim)
    {
        sim.WriteAt(TimeSpan.FromSeconds(1), "SEQ01.Start", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromSeconds(2), "SEQ01.Start", TagValue.Bool(false));
        sim.InjectFaultAt(
            TimeSpan.FromSeconds(40),
            "CV001.Motor",
            "thermal-bias",
            new FaultArguments(new FaultArgument("amount", 0.8)));
    }

    /// <summary>The same plant and blocks, declared in <c>conveyor-control.json</c>.</summary>
    private static Simulation BuildFromJson()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Plants", "conveyor-control.json"));
        LoadResult result = PlantLoader.Load(json, Catalogue, new LoadOptions());
        Assert.True(result.IsValid, result.ToText());

        Simulation sim = result.Builder!.Build();
        Drive(sim);
        return sim;
    }

    [Fact]
    public void TheBlocksOwnTagsJoinThePlantsDirectory()
    {
        Simulation sim = Build();

        Assert.Equal(4, sim.ScanBlockCount);
        foreach (string name in new[]
                 {
                     "PERM01.Ok", "PERM01.FirstOut",
                     "INT01.Ok", "INT01.Tripped", "INT01.FirstOut", "INT01.Reset",
                     "CUR01.Hi.Active", "CUR01.Hi.Acked", "CUR01.HiHi.Active", "CUR01.HiHi.Acked", "CUR01.Ack",
                     "SEQ01.Step", "SEQ01.Running", "SEQ01.Held", "SEQ01.Complete", "SEQ01.Faulted", "SEQ01.StepTime",
                     "SEQ01.Start", "SEQ01.Hold", "SEQ01.Resume", "SEQ01.Abort", "SEQ01.Reset",
                 })
        {
            Assert.True(sim.IO.Directory.TryFind(name, out _), $"The directory has no tag '{name}'.");
        }

        // 27 plant tags (millrace tags conveyor-line.json) plus 22 owned ones.
        Assert.Equal(49, sim.IO.Directory.Count);
    }

    [Fact]
    public void TheWorkedExampleMatchesItsGolden()
    {
        Simulation sim = Build();

        sim.RunFor(TimeSpan.FromSeconds(120));

        Assert.Contains(sim.Events.Records, r =>
            r.Source == "CUR01" && r.Code == "ALARM_RAISED" && r.Message.StartsWith("HiHi:", StringComparison.Ordinal));
        Assert.Contains(sim.Events.Records, r =>
            r.Source == "INT01" && r.Code == "INTERLOCK_TRIP" && r.Message == "CV001.Tripped abnormal.");
        Assert.Contains(sim.Events.Records, r =>
            r.Source == "SEQ01" && r.Code == "SEQUENCE_COMPLETE");

        Golden.Assert("Golden/conveyor-control.log", sim.Events.ToText());
    }

    [Fact]
    public void TheWorkedExampleRunsTwiceByteIdentically()
    {
        Simulation first = Build();
        Simulation second = Build();

        first.RunFor(TimeSpan.FromSeconds(120));
        second.RunFor(TimeSpan.FromSeconds(120));

        Assert.Equal(first.Events.ToText(), second.Events.ToText());
        Assert.Equal(first.IO.Snapshot().ToArray(), second.IO.Snapshot().ToArray());
    }

    [Fact]
    public void TheJsonWorkedExampleHasTheSameBlocksAndDirectory()
    {
        Simulation fromJson = BuildFromJson();
        Simulation inCode = Build();

        Assert.Equal(4, fromJson.ScanBlockCount);
        Assert.Equal(49, fromJson.IO.Directory.Count);
        Assert.Equal(inCode.IO.Directory.ToText(), fromJson.IO.Directory.ToText());
    }

    [Fact]
    public void TheJsonWorkedExampleWritesTheCodeBuiltEventLogByteForByte()
    {
        Simulation fromJson = BuildFromJson();
        Simulation inCode = Build();

        fromJson.RunFor(TimeSpan.FromSeconds(120));
        inCode.RunFor(TimeSpan.FromSeconds(120));

        Assert.Equal(inCode.Events.ToText(), fromJson.Events.ToText());
        Assert.Equal(inCode.IO.Snapshot().ToArray(), fromJson.IO.Snapshot().ToArray());
        Golden.Assert("Golden/conveyor-control.log", fromJson.Events.ToText());
    }
}
