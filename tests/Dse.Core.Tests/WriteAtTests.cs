using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

public class WriteAtTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = Start,
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static Simulation Plant() => new SimulationBuilder(Options).Add(new Thermostat("T")).Build();

    private static Simulation PlantWithFuse() =>
        new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Fuse("F")).Build();

    [Fact]
    public void AnUnknownTagThrowsAtScheduleTime()
    {
        Simulation sim = Plant();

        Assert.Throws<KeyNotFoundException>(
            () => sim.WriteAt(TimeSpan.FromSeconds(1), "T.Nope", TagValue.Bool(true)));
    }

    [Fact]
    public void AReadOnlyTagThrowsAtScheduleTime()
    {
        Simulation sim = Plant();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => sim.WriteAt(TimeSpan.FromSeconds(1), "T.Output", TagValue.Double(1.0)));
        Assert.Contains("read-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AKindMismatchThrowsAtScheduleTime()
    {
        Simulation sim = Plant();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => sim.WriteAt(TimeSpan.FromSeconds(1), "T.Enable", TagValue.Double(1.0)));
        Assert.Contains("Bool tag", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWriteLandsOnTheNamedTickWithAWriteRecord()
    {
        Simulation sim = Plant();
        sim.WriteAt(TimeSpan.FromMilliseconds(50), "T.Enable", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(50));
        Assert.Empty(sim.Events.Records);          // ticks 0..4: nothing yet

        sim.Tick();                                 // tick 5 = 50 ms

        Assert.Equal(
            new[] { (5L, "T.Enable", "WRITE", "Set to true.") },
            sim.Events.Records.Select(r => (r.Tick, r.Source, r.Code, r.Message)));
        Assert.Equal(40.0, sim.IO.ReadDouble("T.Output"));
    }

    [Fact]
    public void AQueuedWriteOnTheSameTickLandsFirst()
    {
        Simulation sim = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(10));   // tick 0 has run; the clock is at tick 1

        sim.IO.WriteDouble("T.Setpoint", 30.0);      // queued: phase 1 of tick 1
        sim.WriteAt(TimeSpan.FromMilliseconds(10), "T.Enable", TagValue.Bool(true));
        sim.Tick();

        Assert.Equal(
            new[] { ("T.Setpoint", "Set to 30."), ("T.Enable", "Set to true.") },
            sim.Events.Records.Select(r => (r.Source, r.Message)));
        Assert.Equal(60.0, sim.IO.ReadDouble("T.Output"));
    }

    [Fact]
    public void AScheduledWriteAndAFaultInterleaveInScheduleOrder()
    {
        Simulation faultFirst = PlantWithFuse();
        faultFirst.InjectFaultAt(TimeSpan.FromMilliseconds(20), "F", Fuse.Blow);
        faultFirst.WriteAt(TimeSpan.FromMilliseconds(20), "T.Enable", TagValue.Bool(true));
        faultFirst.RunFor(TimeSpan.FromMilliseconds(30));

        Simulation writeFirst = PlantWithFuse();
        writeFirst.WriteAt(TimeSpan.FromMilliseconds(20), "T.Enable", TagValue.Bool(true));
        writeFirst.InjectFaultAt(TimeSpan.FromMilliseconds(20), "F", Fuse.Blow);
        writeFirst.RunFor(TimeSpan.FromMilliseconds(30));

        Assert.Equal(new[] { "FAULT", "WRITE" }, faultFirst.Events.Records.Select(r => r.Code));
        Assert.Equal(new[] { "WRITE", "FAULT" }, writeFirst.Events.Records.Select(r => r.Code));
    }

    [Fact]
    public void WriteInIsRelativeToNow()
    {
        Simulation sim = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(100));   // the clock is at tick 10

        sim.WriteIn(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));
        sim.RunFor(TimeSpan.FromMilliseconds(40));

        Assert.Equal(13L, Assert.Single(sim.Events.Records).Tick);
    }

    [Fact]
    public void TwoRunsWithTheSameScheduledWritesAreByteIdentical()
    {
        static string Run()
        {
            Simulation sim = Plant();
            sim.WriteAt(TimeSpan.FromMilliseconds(50), "T.Enable", TagValue.Bool(true));
            sim.WriteAt(TimeSpan.FromMilliseconds(120), "T.Setpoint", TagValue.Double(33.5));
            sim.WriteAt(TimeSpan.FromMilliseconds(200), "T.Enable", TagValue.Bool(false));
            sim.RunFor(TimeSpan.FromMilliseconds(300));
            return sim.Events.ToText();
        }

        string first = Run();
        string second = Run();

        Assert.Equal(first, second);
        Assert.Contains("T.Setpoint  WRITE  Set to 33.5.", first, StringComparison.Ordinal);
    }
}
