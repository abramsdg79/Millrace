using Dse.Components;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Core.Faults;
using Dse.Io;
using Dse.Tests.Shared;

namespace Dse.Control.Tests;

/// <summary>
/// Spec 5c §5's worked example: the conveyor plant under a permissive, an
/// interlock, a current alarm and a start-up sequence, for two simulated
/// minutes. The blocks are attached in code because 5c gives the plant file no
/// way to describe them.
/// </summary>
public class WorkedExampleTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(200);

    private static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Build();

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
        sim.WriteAt(TimeSpan.FromSeconds(1), "SEQ01.Start", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromSeconds(2), "SEQ01.Start", TagValue.Bool(false));
        sim.InjectFaultAt(
            TimeSpan.FromSeconds(40),
            "CV001.Motor",
            "thermal-bias",
            new FaultArguments(new FaultArgument("amount", 0.8)));
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

        // 25 plant tags (dse tags conveyor-line.json) plus 22 owned ones.
        Assert.Equal(47, sim.IO.Directory.Count);
    }

    [Fact]
    public void TheWorkedExampleMatchesItsGolden()
    {
        Simulation sim = Build();

        sim.RunFor(TimeSpan.FromSeconds(120));

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
}
