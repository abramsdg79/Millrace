using Dse.Components.Instruments;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Io;
using Dse.Core.Time;
using Dse.Io;
using Xunit;

namespace Dse.Components.Tests;

public class InstrumentQualityTests
{
    private static readonly InstrumentSpec Volts = new("V", 0.0, 10.0);

    private static SimulationOptions Options => new() { Seed = 1UL };

    private static (Simulation Sim, ProbeInstrument Probe, Setpoint Truth) Plant()
    {
        var truth = new Setpoint("Truth", 5.0);
        var probe = new ProbeInstrument("PT", Volts);
        truth.Out.ConnectTo(probe.In);
        Simulation sim = new SimulationBuilder(Options).Add(truth).Add(probe).Build();
        return (sim, probe, truth);
    }

    [Fact]
    public void ReadingOutsideTheRangeIsClampedAndUncertain()
    {
        (Simulation sim, ProbeInstrument probe, Setpoint truth) = Plant();
        truth.Value = 12.0;

        sim.Tick();

        Assert.Equal(10.0, probe.Value.Value);
        Assert.Equal(TagQuality.Uncertain(QualityDetail.OutOfRange), probe.Health.Value);

        truth.Value = -1.0;
        sim.Tick();
        Assert.Equal((0.0, TagQuality.Uncertain(QualityDetail.OutOfRange)), (probe.Value.Value, probe.Health.Value));

        truth.Value = 5.0;
        sim.Tick();
        Assert.Equal((5.0, TagQuality.Good), (probe.Value.Value, probe.Health.Value));
    }

    [Fact]
    public void FailFaultsAreBadNotUncertain()
    {
        (Simulation sim, ProbeInstrument probe, Setpoint truth) = Plant();
        truth.Value = 12.0;
        sim.InjectFaultIn(TimeSpan.Zero, "PT", InstrumentFaults.FailLow);

        sim.Tick();

        Assert.Equal((0.0, TagQuality.Bad(QualityDetail.SensorFailure)), (probe.Value.Value, probe.Health.Value));
    }

    [Fact]
    public void AFrozenReadingStaysGood()
    {
        (Simulation sim, ProbeInstrument probe, Setpoint truth) = Plant();
        sim.Tick();
        sim.InjectFaultIn(TimeSpan.Zero, "PT", InstrumentFaults.Freeze);
        truth.Value = 50.0;

        sim.Tick();

        Assert.Equal((5.0, TagQuality.Good), (probe.Value.Value, probe.Health.Value));
    }

    [Fact]
    public void InstrumentsDeclareTheirValueTagWithQuality()
    {
        (Simulation sim, _, Setpoint truth) = Plant();

        TagDescriptor value = Assert.Single(sim.IO.Directory.Tags);
        Assert.Equal(("PT.Value", TagKind.Double, TagAccess.ReadOnly, "V", 0.0, 10.0, "Measured value"),
            (value.Name, value.Kind, value.Access, value.Unit, value.RangeLow, value.RangeHigh, value.Description));

        truth.Value = 12.0;
        sim.Tick();

        Assert.Equal(TagValue.Double(10.0, TagQuality.Uncertain(QualityDetail.OutOfRange)), sim.IO.Read("PT.Value"));
    }

    [Fact]
    public void TheZeroSpeedSwitchAddsStopped()
    {
        var zss = new ZeroSpeedSwitch("ZS", new InstrumentSpec("m/s", 0.0, 3.0), thresholdSpeed: 0.02, delaySeconds: 1.0);

        Assert.Equal(new[] { ("Value", TagKind.Double), ("Stopped", TagKind.Bool) },
            zss.DescribeTags().Select(t => (t.Name, t.Kind)));
    }

    [Fact]
    public void ThePartCounterDeclaresCountAndPresent()
    {
        var belt = new Dse.Core.Flow.DiscreteBelt("B", length: 2.0, maxSpeed: 1.0);
        var counter = new PartCounter("PC", belt, positionM: 1.0, windowM: 0.1);

        Assert.Equal(new[] { ("Count", TagKind.Int64, "count"), ("Present", TagKind.Bool, string.Empty) },
            counter.DescribeTags().Select(t => (t.Name, t.Kind, t.Unit)));
    }
}
