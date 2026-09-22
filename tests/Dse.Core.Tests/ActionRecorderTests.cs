using Dse.Core.Faults;
using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

public class ActionRecorderTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    /// <summary>Every call, flattened to a comparable tuple.</summary>
    private sealed class Spy : IActionRecorder
    {
        public List<(string Kind, long Tick, string Target, string Detail)> Calls { get; } = [];

        public void Wrote(long tick, string tag, TagValue value) =>
            Calls.Add(("wrote", tick, tag, value.ToString()));

        public void Faulted(long tick, string componentId, string faultId, FaultArguments arguments) =>
            Calls.Add(("faulted", tick, componentId, $"{faultId}({arguments})"));

        public void Cleared(long tick, string componentId, string faultId) =>
            Calls.Add(("cleared", tick, componentId, faultId));
    }

    private static (Simulation Sim, Spy Spy) Plant()
    {
        Simulation sim = new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Fuse("F")).Build();
        var spy = new Spy();
        sim.AttachActionRecorder(spy);
        return (sim, spy);
    }

    [Fact]
    public void AQueuedWriteIsRecordedWithTheTickItLanded()
    {
        (Simulation sim, Spy spy) = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        sim.IO.WriteBool("T.Enable", true);
        sim.Tick();

        Assert.Equal(("wrote", 3L, "T.Enable", "true"), Assert.Single(spy.Calls));
    }

    [Fact]
    public void AScheduledWriteIsRecordedWithTheTickItLanded()
    {
        (Simulation sim, Spy spy) = Plant();
        sim.WriteAt(TimeSpan.FromMilliseconds(70), "T.Setpoint", TagValue.Double(12.5));

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(("wrote", 7L, "T.Setpoint", "12.5"), Assert.Single(spy.Calls));
    }

    [Fact]
    public void AFaultCarriesTheResolvedArgumentsAndAClearFollowsIt()
    {
        (Simulation sim, Spy spy) = Plant();
        sim.InjectFaultAt(TimeSpan.FromMilliseconds(20), "F", Fuse.Blow);
        sim.ClearFaultAt(TimeSpan.FromMilliseconds(40), "F", Fuse.Blow);

        sim.RunFor(TimeSpan.FromMilliseconds(50));

        Assert.Equal(
            new[]
            {
                ("faulted", 2L, "F", "blow(resistance=1000000)"),
                ("cleared", 4L, "F", "blow"),
            },
            spy.Calls);
    }

    [Fact]
    public void EveryPathReachesTheRecorderInLandingOrder()
    {
        (Simulation sim, Spy spy) = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(10));
        sim.IO.WriteBool("T.Enable", true);                                        // queued: tick 1
        sim.WriteAt(TimeSpan.FromMilliseconds(10), "T.Setpoint", TagValue.Double(5.0));
        sim.InjectFaultAt(TimeSpan.FromMilliseconds(10), "F", Fuse.Blow, new FaultArguments(new FaultArgument("resistance", 2.0)));

        sim.Tick();

        Assert.Equal(
            new[]
            {
                ("wrote", 1L, "T.Enable"),
                ("wrote", 1L, "T.Setpoint"),
                ("faulted", 1L, "F"),
            },
            spy.Calls.Select(c => (c.Kind, c.Tick, c.Target)));
        Assert.Equal("blow(resistance=2)", spy.Calls[2].Detail);
    }

    [Fact]
    public void ASecondAttachThrows()
    {
        (Simulation sim, _) = Plant();

        Assert.Throws<InvalidOperationException>(() => sim.AttachActionRecorder(new Spy()));
    }

    [Fact]
    public void RecordingChangesNothingAboutTheRun()
    {
        static string Run(bool record)
        {
            Simulation sim = new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Fuse("F")).Build();
            if (record)
            {
                sim.AttachActionRecorder(new Spy());
            }

            sim.WriteAt(TimeSpan.FromMilliseconds(50), "T.Enable", TagValue.Bool(true));
            sim.InjectFaultAt(TimeSpan.FromMilliseconds(70), "F", Fuse.Blow);
            sim.RunFor(TimeSpan.FromMilliseconds(100));
            return sim.Events.ToText();
        }

        Assert.Equal(Run(record: false), Run(record: true));
    }
}
