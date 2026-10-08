using Millrace.Components.Instruments;
using Millrace.Components.Tests.Fakes;
using Millrace.Core;
using Millrace.Core.Faults;
using Millrace.Core.Time;
using Millrace.Io;
using Xunit;

namespace Millrace.Components.Tests;

public class SignalInstrumentTests
{
    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(100),
    };

    [Fact]
    public void EachSensorMeasuresItsInputAndCanBeLiedTo()
    {
        var speed = new SpeedSensor("ST", new InstrumentSpec("m/s", 0.0, 5.0));
        var current = new CurrentSensor("IT", new InstrumentSpec("A", 0.0, 50.0));
        var temperature = new TemperatureSensor("TT", new InstrumentSpec("degC", -20.0, 400.0));
        var v = new Setpoint("V", 1.8);
        var i = new Setpoint("I", 12.5);
        var t = new Setpoint("T", 180.0);
        v.Out.ConnectTo(speed.Speed);
        i.Out.ConnectTo(current.Current);
        t.Out.ConnectTo(temperature.Temperature);
        Simulation sim = new SimulationBuilder(Options())
            .Add(speed).Add(current).Add(temperature).Add(v).Add(i).Add(t).Build();

        sim.Tick();
        Assert.Equal((1.8, 12.5, 180.0), (speed.Value.Value, current.Value.Value, temperature.Value.Value));

        sim.InjectFaultIn(TimeSpan.Zero, "ST", InstrumentFaults.FailHigh);
        sim.InjectFaultIn(TimeSpan.Zero, "IT", InstrumentFaults.Calibration, new FaultArguments(new FaultArgument("gain", 2.0)));
        sim.InjectFaultIn(TimeSpan.Zero, "TT", InstrumentFaults.Freeze);
        t.Value = 250.0;
        sim.Tick();

        Assert.Equal((5.0, TagQuality.Bad(QualityDetail.SensorFailure)), (speed.Value.Value, speed.Health.Value));
        Assert.Equal(25.0, current.Value.Value);
        Assert.Equal(180.0, temperature.Value.Value);
        Assert.Equal(250.0, sim.Telemetry.Read("TT.Truth"));
    }

    [Fact]
    public void TheZeroSpeedSwitchTripsAfterTheDelayAndResetsOnMotion()
    {
        var zss = new ZeroSpeedSwitch("ZSS", new InstrumentSpec("m/s", 0.0, 5.0), thresholdSpeed: 0.1, delaySeconds: 0.5);
        var v = new Setpoint("V", 1.5);
        v.Out.ConnectTo(zss.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(zss).Add(v).Build();

        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.False(zss.Stopped.Value);

        v.Value = 0.0;
        sim.RunFor(TimeSpan.FromMilliseconds(400));
        Assert.False(zss.Stopped.Value);
        sim.RunFor(TimeSpan.FromMilliseconds(200));
        Assert.True(zss.Stopped.Value);
        Assert.Single(sim.Events.Records, r => r.Code == "ZERO_SPEED");

        v.Value = 1.0;
        sim.Tick();
        Assert.False(zss.Stopped.Value);
        Assert.Single(sim.Events.Records, r => r.Code == "MOTION");
    }

    [Fact]
    public void AFrozenZeroSpeedSwitchNeverNoticesTheBeltStop()
    {
        var zss = new ZeroSpeedSwitch("ZSS", new InstrumentSpec("m/s", 0.0, 5.0), 0.1, 0.5);
        var v = new Setpoint("V", 1.5);
        v.Out.ConnectTo(zss.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(zss).Add(v).Build();
        sim.RunFor(TimeSpan.FromSeconds(1));

        sim.InjectFaultIn(TimeSpan.Zero, "ZSS", InstrumentFaults.Freeze);
        v.Value = 0.0;
        sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.False(zss.Stopped.Value);
        Assert.Equal(1.5, zss.Value.Value);
        Assert.Equal(0.0, sim.Telemetry.Read("ZSS.Truth"));
    }
}
