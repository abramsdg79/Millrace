using Dse.Control.Tests.Fakes;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Logging;
using Dse.Core.Time;
using Dse.Io;
using Dse.Realtime;

namespace Dse.Control.Tests;

public class HostTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    /// <summary>Records every write that landed. This plant injects no faults.</summary>
    private sealed class Spy : IActionRecorder
    {
        public List<(string Tag, long Tick, string Value)> Writes { get; } = [];

        public void Wrote(long tick, string tag, TagValue value) => Writes.Add((tag, tick, value.ToString()));

        public void Faulted(long tick, string componentId, string faultId, FaultArguments arguments) =>
            throw new NotSupportedException("The vessel plant injects no faults.");

        public void Cleared(long tick, string componentId, string faultId) =>
            throw new NotSupportedException("The vessel plant clears no faults.");
    }

    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    /// <summary>One vessel filling at 10 % a second.</summary>
    private static SimulationBuilder Plant() => new SimulationBuilder(Options).Add(new Vessel("V1", 10.0));

    private static bool Has(Simulation sim, string source, string code) =>
        sim.Events.Records.Any(r =>
            string.Equals(r.Source, source, StringComparison.Ordinal) &&
            string.Equals(r.Code, code, StringComparison.Ordinal));

    [Fact]
    public void ATimerOverAPlantPublishesQAndElapsedTime()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Timer("TMR01", TimerMode.OnDelay, "V1.Running", TimeSpan.FromSeconds(0.5), Period))
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));

        // Running is published at the end of tick 10, so the scan at tick 20 is
        // the first that sees it; five more periods reach the 0.5 s preset.
        sim.RunFor(TimeSpan.FromMilliseconds(600));    // ticks 0..59
        Assert.False(sim.IO.ReadBool("TMR01.Q"));

        sim.RunFor(TimeSpan.FromMilliseconds(10));     // tick 60
        Assert.True(sim.IO.ReadBool("TMR01.Q"));
        Assert.Equal(0.5, sim.IO.ReadDouble("TMR01.ET"), 12);
    }

    [Fact]
    public void APermissiveOverAPlantPublishesOkAndFirstOut()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Permissive(
                "PERM01",
                [new Condition("V1.Tripped", false), new Condition("V1.Running", true)],
                Period))
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));

        sim.Tick();                                    // tick 0: nothing is running yet
        Assert.False(sim.IO.ReadBool("PERM01.Ok"));
        Assert.Equal(1L, sim.IO.ReadInt64("PERM01.FirstOut"));
        SimEventRecord lost = Assert.Single(sim.Events.Records);
        Assert.Equal("PERM01", lost.Source);
        Assert.Equal("PERMISSIVE_LOST", lost.Code);
        Assert.Equal("V1.Running dropped.", lost.Message);

        sim.RunFor(TimeSpan.FromMilliseconds(500));
        Assert.True(sim.IO.ReadBool("PERM01.Ok"));
        Assert.Equal(-1L, sim.IO.ReadInt64("PERM01.FirstOut"));
        Assert.True(Has(sim, "PERM01", "PERMISSIVE_OK"));
    }

    [Fact]
    public void AnInterlockOverAPlantTripsAndHoldsTheFillCommandLow()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Interlock(
                "INT01",
                [new Condition("V1.Tripped", false)],
                [new BlockWrite("V1.Fill", TagValue.Bool(false))],
                Period))
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(500), "V1.Trip", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromSeconds(1));

        Assert.True(sim.IO.ReadBool("INT01.Tripped"));
        Assert.False(sim.IO.ReadBool("INT01.Ok"));
        Assert.False(sim.IO.ReadBool("V1.Fill"));
        Assert.True(Has(sim, "INT01", "INTERLOCK_TRIP"));
        Assert.Single(sim.Events.Records, r =>
            string.Equals(r.Source, "V1.Fill", StringComparison.Ordinal) &&
            string.Equals(r.Message, "Set to false by INT01.", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAlarmOverAPlantRaisesAndIsAcknowledged()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Alarm(
                "LVL01",
                "V1.Level",
                [new AlarmLimit(AlarmLimitKind.Hi, 20.0, 5.0, TimeSpan.Zero)],
                Period))
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));

        // The Ack lands at phase 1 of tick 270 and is published at the end of
        // that tick, so the 100 ms scan at tick 280 is the first that sees it.
        // The run covers ticks 0..299, so 2700 ms is inside it and 2900 would
        // not be: the scan that would see a 2900 ms write is tick 300.
        sim.WriteAt(TimeSpan.FromMilliseconds(2700), "LVL01.Ack", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromSeconds(3));

        Assert.True(sim.IO.ReadBool("LVL01.Hi.Active"));
        Assert.True(sim.IO.ReadBool("LVL01.Hi.Acked"));
        Assert.True(Has(sim, "LVL01", "ALARM_RAISED"));
        Assert.True(Has(sim, "LVL01", "ALARM_ACKED"));
    }

    [Fact]
    public void ASequencerOverAPlantStepsAndCompletes()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Sequencer(
                "SEQ01",
                [
                    new SequenceStep(
                        "Open the valve",
                        [new BlockWrite("V1.Fill", TagValue.Bool(true))],
                        StepTransition.When("V1.Level", PredicateOperator.GreaterOrEqual, TagValue.Double(5.0)),
                        TimeSpan.FromSeconds(5)),
                    new SequenceStep("Hold the level", [], StepTransition.After(TimeSpan.FromSeconds(0.5))),
                    new SequenceStep(
                        "Close the valve",
                        [new BlockWrite("V1.Fill", TagValue.Bool(false))],
                        StepTransition.When("V1.Running", PredicateOperator.Equal, TagValue.Bool(false)),
                        TimeSpan.FromSeconds(5)),
                ],
                Period,
                [new BlockWrite("V1.Fill", TagValue.Bool(false))]))
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "SEQ01.Start", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(300), "SEQ01.Start", TagValue.Bool(false));

        sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.True(sim.IO.ReadBool("SEQ01.Complete"));
        Assert.False(sim.IO.ReadBool("SEQ01.Running"));
        Assert.Equal(0L, sim.IO.ReadInt64("SEQ01.Step"));
        Assert.Equal(
            3,
            sim.Events.Records.Count(r =>
                string.Equals(r.Source, "SEQ01", StringComparison.Ordinal) &&
                string.Equals(r.Code, "STEP_ENTERED", StringComparison.Ordinal)));
        Assert.True(Has(sim, "SEQ01", "SEQUENCE_COMPLETE"));
    }

    [Fact]
    public void ABlockOutputReachesTheRealtimeLiveStateAndItsTickFrames()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Permissive("PERM01", [new Condition("V1.Running", true)], Period))
            .Build();
        var hub = new RealtimeHub(sim.IO.Directory);
        sim.AttachFrameSink(hub);
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(500));    // ticks 0..49
        hub.Pump();

        // The write lands at phase 1 of tick 10 and Running is published at the
        // end of it, so the 100 ms scan at tick 20 is the first that sees it and
        // PERM01.Ok goes true in the frame published at the end of tick 20.
        TagState ok = hub.State.Get("PERM01.Ok");
        Assert.True(ok.Value.AsBool);
        Assert.Equal(20L, ok.LastChangeTick);
        Assert.Equal(49L, hub.State.Tick);
        Assert.Equal(-1L, hub.State.Get("PERM01.FirstOut").Value.AsInt64);

        Assert.Contains(
            hub.State.Snapshot().RecentEvents,
            e => string.Equals(e.Source, "PERM01", StringComparison.Ordinal)
                 && string.Equals(e.Code, "PERMISSIVE_OK", StringComparison.Ordinal));
    }

    [Fact]
    public void ABlockWriteIsNotRecorded()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Interlock(
                "INT01",
                [new Condition("V1.Tripped", false)],
                [new BlockWrite("V1.Fill", TagValue.Bool(false))],
                Period))
            .Build();
        var spy = new Spy();
        sim.AttachActionRecorder(spy);
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(500), "V1.Trip", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromSeconds(1));

        // Timing rule: the trip lands at phase 1 of tick 50 and is published at the
        // end of it; the 100 ms scan at tick 60 is the first to see it, trips, and
        // queues its write, which lands at phase 1 of tick 61. That write is the
        // block's own: it is logged "by INT01" and never reaches the recorder (R77).
        Assert.Equal(
            new[] { ("V1.Fill", 10L, "true"), ("V1.Trip", 50L, "true") },
            spy.Writes.ToArray());
        SimEventRecord write = Assert.Single(sim.Events.Records, r =>
            r.Tick == 61L && string.Equals(r.Source, "V1.Fill", StringComparison.Ordinal));
        Assert.Equal("WRITE", write.Code);
        Assert.Equal("Set to false by INT01.", write.Message);
    }

    [Fact]
    public void TwoRunsOfThePlantWithEveryBlockAreByteIdentical()
    {
        static Simulation Build()
        {
            Simulation sim = Plant()
                .AddScanBlock(new Timer("TMR01", TimerMode.OnDelay, "V1.Running", TimeSpan.FromSeconds(0.5), Period))
                .AddScanBlock(new Permissive("PERM01", [new Condition("V1.Tripped", false)], Period))
                .AddScanBlock(new Interlock(
                    "INT01",
                    [new Condition("V1.Tripped", false)],
                    [new BlockWrite("V1.Fill", TagValue.Bool(false))],
                    Period))
                .AddScanBlock(new Alarm(
                    "LVL01",
                    "V1.Level",
                    [new AlarmLimit(AlarmLimitKind.Hi, 20.0, 5.0, TimeSpan.Zero)],
                    Period))
                .Build();
            sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));
            sim.WriteAt(TimeSpan.FromMilliseconds(2500), "V1.Trip", TagValue.Bool(true));
            return sim;
        }

        Simulation first = Build();
        Simulation second = Build();
        first.RunFor(TimeSpan.FromSeconds(3));
        second.RunFor(TimeSpan.FromSeconds(3));

        Assert.Equal(4, first.ScanBlockCount);
        Assert.Equal(first.Events.ToText(), second.Events.ToText());
        Assert.Equal(first.IO.Snapshot().ToArray(), second.IO.Snapshot().ToArray());
    }
}
