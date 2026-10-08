using Millrace.Core.Logging;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Time;
using Millrace.Io;
using Xunit;

namespace Millrace.Core.Tests;

public class ScanBlockHostTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static readonly TimeSpan TwoSteps = TimeSpan.FromMilliseconds(20);

    /// <summary>Two thermostats: the block reads T.Enable and commands U.Enable.</summary>
    private static SimulationBuilder Plant() =>
        new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Thermostat("U"));

    private static EchoBlock Echo(string id) =>
        new EchoBlock(id, TwoSteps)
            .Reads("T.Enable")
            .MayWrite("U.Enable")
            .Publishes("Q", TagKind.Bool, "", "The echo")
            .Accepts("Cmd", TagKind.Bool, "", "A command");

    [Fact]
    public void APlantWithNoBlockScansNothing()
    {
        Simulation sim = Plant().Build();

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(0, sim.ScanBlockCount);
        Assert.Empty(sim.Events.Records);
        Assert.Equal(6, sim.IO.Directory.Count);
    }

    [Fact]
    public void TheFirstScanRunsAtTickZero()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();

        sim.Tick();

        Assert.Equal(new long[] { 0L }, block.ScanTicks);
    }

    [Fact]
    public void ScansHappenEveryPeriodAndNotBetween()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(100));   // ticks 0..9

        Assert.Equal(new long[] { 0L, 2L, 4L, 6L, 8L }, block.ScanTicks);
    }

    [Fact]
    public void AnInputIsSeenAsOfTheEndOfThePreviousTick()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(50));    // ticks 0..4

        Assert.Equal(new long[] { 0L, 2L, 4L }, block.ScanTicks);
        Assert.Equal(new[] { false, false, true }, block.Seen);
    }

    [Fact]
    public void AnOutputIsPublishedAtTheEndOfTheScanTick()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(40));    // ticks 0..3
        Assert.False(sim.IO.ReadBool("B.Q"));

        sim.RunFor(TimeSpan.FromMilliseconds(10));    // tick 4: the scan that sees it
        Assert.True(sim.IO.ReadBool("B.Q"));
    }

    [Fact]
    public void AWriteQueuedByABlockLandsAtPhaseOneOfTheNextTick()
    {
        EchoBlock block = Echo("B");
        block.WriteOnce = true;
        Simulation sim = Plant().AddScanBlock(block).Build();

        sim.Tick();                                   // tick 0: the scan queues the write
        Assert.Empty(sim.Events.Records);
        Assert.False(sim.IO.ReadBool("U.Enable"));

        sim.Tick();                                   // tick 1: phase 1 applies it
        SimEventRecord record = Assert.Single(sim.Events.Records);
        Assert.Equal(1L, record.Tick);
        Assert.Equal("U.Enable", record.Source);
        Assert.Equal("WRITE", record.Code);
        Assert.Equal("Set to true by B.", record.Message);
        Assert.True(sim.IO.ReadBool("U.Enable"));
    }

    [Fact]
    public void AnOutputNotSetHoldsItsPreviousValue()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(50), "T.Enable", TagValue.Bool(false));

        sim.RunFor(TimeSpan.FromMilliseconds(50));    // ticks 0..4; Q went true at tick 4
        Assert.True(sim.IO.ReadBool("B.Q"));

        block.Silent = true;
        sim.RunFor(TimeSpan.FromMilliseconds(50));    // ticks 5..9; the block publishes nothing
        Assert.Equal(new[] { false, false, true, false, false }, block.Seen);
        Assert.True(sim.IO.ReadBool("B.Q"));
    }

    [Fact]
    public void AnEventIsLoggedUnderTheBlockId()
    {
        EchoBlock block = Echo("B");
        block.RaiseOnce = true;
        Simulation sim = Plant().AddScanBlock(block).Build();

        sim.Tick();

        SimEventRecord record = Assert.Single(sim.Events.Records);
        Assert.Equal(0L, record.Tick);
        Assert.Equal("B", record.Source);
        Assert.Equal("ECHO", record.Code);
        Assert.Equal("The stub raised an event.", record.Message);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), record.SimTime);
    }

    [Fact]
    public void ACommandIsWritableAndTheBlockSeesItOneScanLater()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(30), "B.Cmd", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(50));    // ticks 0..4

        Assert.True(sim.IO.ReadBool("B.Cmd"));        // the committed write is an ordinary tag read
        Assert.Equal(new[] { false, false, true }, block.SeenCommand);
        SimEventRecord record = Assert.Single(sim.Events.Records);
        Assert.Equal(3L, record.Tick);
        Assert.Equal("B.Cmd", record.Source);
    }

    [Fact]
    public void AnOwnedOutputRefusesAWrite()
    {
        Simulation sim = Plant().AddScanBlock(Echo("B")).Build();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => sim.WriteAt(TimeSpan.FromMilliseconds(10), "B.Q", TagValue.Bool(true)));
        Assert.Contains("read-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StoringTheWrongKindOnAnOutputNamesTheBlockThePinAndBothKinds()
    {
        EchoBlock block = Echo("B");
        block.WrongKind = true;
        Simulation sim = Plant().AddScanBlock(block).Build();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(sim.Tick);

        Assert.Contains("'B'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'B.Q'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Bool", error.Message, StringComparison.Ordinal);
        Assert.Contains("Double", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AThrowingScanAbortsTheTickBeforeTheClockAdvancesAndTheBlockIsNotRescheduled()
    {
        EchoBlock block = Echo("B");
        block.ThrowOnScan = true;
        Simulation sim = Plant().AddScanBlock(block).Build();
        long before = sim.Clock.TickCount;

        Assert.Throws<InvalidOperationException>(() => sim.Tick());

        Assert.Equal(before, sim.Clock.TickCount);
    }

    [Fact]
    public void ElapsedIsZeroOnTheFirstScanAndThePeriodAfterwards()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(60));    // ticks 0..5; scans at 0, 2, 4

        Assert.Equal(3, block.ElapsedSeconds.Count);
        Assert.Equal(0.0, block.ElapsedSeconds[0]);
        Assert.Equal(0.02, block.ElapsedSeconds[1], 12);
        Assert.Equal(0.02, block.ElapsedSeconds[2], 12);
    }

    [Fact]
    public void BlocksScanInAddOrderAmongEquals()
    {
        var first = new EchoBlock("A", TimeSpan.FromMilliseconds(10)) { RaiseEveryScan = true };
        var second = new EchoBlock("B", TimeSpan.FromMilliseconds(10)) { RaiseEveryScan = true };
        Simulation sim = Plant().AddScanBlock(first).AddScanBlock(second).Build();

        sim.Tick();

        Assert.Equal(new[] { "A", "B" }, sim.Events.Records.Select(r => r.Source).ToArray());
    }

    [Fact]
    public void ABlockReadsAnotherBlocksOutputOneScanLate()
    {
        EchoBlock producer = Echo("B");
        var consumer = new EchoBlock("C", TwoSteps).Reads("B.Q").Publishes("Q");
        Simulation sim = Plant().AddScanBlock(producer).AddScanBlock(consumer).Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(70));    // ticks 0..6; scans at 0, 2, 4, 6

        Assert.Equal(new[] { false, false, true, true }, producer.Seen);
        Assert.Equal(new[] { false, false, false, true }, consumer.Seen);
    }

    [Fact]
    public void TwoRunsOfThePlantWithBlocksAreByteIdentical()
    {
        static Simulation Build()
        {
            Simulation sim = Plant()
                .AddScanBlock(new EchoBlock("A", TimeSpan.FromMilliseconds(10)) { RaiseEveryScan = true })
                .AddScanBlock(new EchoBlock("B", TwoSteps).Reads("T.Enable").MayWrite("U.Enable").Publishes("Q"))
                .Build();
            sim.WriteAt(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));
            return sim;
        }

        Simulation first = Build();
        Simulation second = Build();
        first.RunFor(TimeSpan.FromSeconds(1));
        second.RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(first.Events.ToText(), second.Events.ToText());
        Assert.Equal(first.IO.Snapshot().ToArray(), second.IO.Snapshot().ToArray());
    }
}
