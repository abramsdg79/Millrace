using Millrace.Core;
using Millrace.Core.Faults;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Core.Tests;

public class FaultChannelTests
{
    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void ArgumentsRejectDuplicateNamesAndFormatInvariantly()
    {
        Assert.Throws<ArgumentException>(() => new FaultArguments(new("a", 1.0), new("a", 2.0)));

        var args = new FaultArguments(new("gain", 1.5), new("offset", -0.25));
        Assert.Equal(2, args.Count);
        Assert.Equal(1.5, args.Get("gain"));
        Assert.True(args.TryGet("offset", out double offset));
        Assert.Equal(-0.25, offset);
        Assert.False(args.TryGet("missing", out _));
        Assert.Equal("gain=1.5, offset=-0.25", args.ToString());
        Assert.Equal("", FaultArguments.None.ToString());
    }

    [Fact]
    public void ADescriptorFillsDefaultsAndRejectsUnknownNames()
    {
        var descriptor = new FaultDescriptor(
            "calibration",
            "Gain and offset error.",
            new FaultParameter("gain", "", 1.0, "Multiplier."),
            new FaultParameter("offset", "unit", 0.0, "Added."));

        FaultArguments resolved = descriptor.Resolve(new FaultArguments(new FaultArgument("offset", 2.0)));
        Assert.Equal("gain=1, offset=2", resolved.ToString());

        var ex = Assert.Throws<ArgumentException>(() => descriptor.Resolve(new FaultArguments(new FaultArgument("gian", 2.0))));
        Assert.Contains("gian", ex.Message);
        Assert.Contains("gain, offset", ex.Message);
    }

    [Fact]
    public void AFaultIsAppliedInPhaseOneOfItsTick()
    {
        var fuse = new Fuse("F1");
        Simulation sim = new SimulationBuilder(Options()).Add(fuse).Build();

        sim.InjectFaultAt(TimeSpan.FromMilliseconds(30), "F1", Fuse.Blow);
        sim.RunFor(TimeSpan.FromMilliseconds(30));
        Assert.True(fuse.Ok.Value);

        sim.Tick();   // tick 3: the fault lands before Evaluate

        Assert.False(fuse.Ok.Value);
        Assert.Equal(1.0e6, fuse.Resistance.Value);
        Assert.Equal("resistance=1000000", fuse.LastArguments!.ToString());
        var record = Assert.Single(sim.Events.Records);
        Assert.Equal((3L, "F1", "FAULT", "blow injected: resistance=1000000."), (record.Tick, record.Source, record.Code, record.Message));
    }

    [Fact]
    public void ArgumentsOverrideDefaults()
    {
        var fuse = new Fuse("F1");
        Simulation sim = new SimulationBuilder(Options()).Add(fuse).Build();

        sim.InjectFaultIn(TimeSpan.Zero, "F1", Fuse.Blow, new FaultArguments(new FaultArgument("resistance", 50.0)));
        sim.Tick();

        Assert.Equal(50.0, fuse.Resistance.Value);
    }

    [Fact]
    public void ClearingRestoresTheComponentAndLogs()
    {
        var fuse = new Fuse("F1");
        Simulation sim = new SimulationBuilder(Options()).Add(fuse).Build();

        sim.InjectFaultAt(TimeSpan.Zero, "F1", Fuse.Blow);
        sim.ClearFaultAt(TimeSpan.FromMilliseconds(20), "F1", Fuse.Blow);
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        Assert.True(fuse.Ok.Value);
        Assert.Equal(["FAULT", "FAULT_CLEARED"], sim.Events.Records.Select(r => r.Code));
        Assert.Equal("blow cleared.", sim.Events.Records[1].Message);
    }

    [Fact]
    public void SchedulingNamesWhatExistsWhenTheTargetOrFaultIsUnknown()
    {
        var fuse = new Fuse("F1");
        var plain = new ConstantSource("C1", 1.0);
        Simulation sim = new SimulationBuilder(Options()).Add(fuse).Add(plain).Build();

        var missing = Assert.Throws<KeyNotFoundException>(() => sim.InjectFaultAt(TimeSpan.Zero, "F2", Fuse.Blow));
        Assert.Contains("F1", missing.Message);

        var notATarget = Assert.Throws<KeyNotFoundException>(() => sim.InjectFaultAt(TimeSpan.Zero, "C1", Fuse.Blow));
        Assert.Contains("IFaultTarget", notATarget.Message);

        var unknownFault = Assert.Throws<ArgumentException>(() => sim.InjectFaultAt(TimeSpan.Zero, "F1", "melt"));
        Assert.Contains("blow", unknownFault.Message);

        Assert.Equal(["blow"], sim.FaultsOf("F1").Select(f => f.Id));
    }

    [Fact]
    public void TwoFaultsOnTheSameTickApplyInScheduleOrder()
    {
        var fuse = new Fuse("F1");
        Simulation sim = new SimulationBuilder(Options()).Add(fuse).Build();

        sim.InjectFaultAt(TimeSpan.Zero, "F1", Fuse.Blow, new FaultArguments(new FaultArgument("resistance", 1.0)));
        sim.InjectFaultAt(TimeSpan.Zero, "F1", Fuse.Blow, new FaultArguments(new FaultArgument("resistance", 2.0)));
        sim.Tick();

        Assert.Equal(2.0, fuse.Resistance.Value);
    }
}
