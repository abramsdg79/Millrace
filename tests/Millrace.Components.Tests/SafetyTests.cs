using Millrace.Components.Safety;
using Millrace.Components.Tests.Fakes;
using Millrace.Core;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Components.Tests;

public class SafetyTests
{
    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void SwitchesAreNormallyClosedAndLogTheirOwnCodes()
    {
        var estop = new EStop("ES1");
        var key = new PullKey("PK1");
        var press = new Switch("Press");
        var pull = new Switch("Pull");
        press.Out.ConnectTo(estop.Actuated);
        pull.Out.ConnectTo(key.Actuated);
        Simulation sim = new SimulationBuilder(Options()).Add(estop).Add(key).Add(press).Add(pull).Build();

        sim.Tick();
        Assert.True(estop.Ok.Value);
        Assert.True(key.Ok.Value);

        press.Value = true;
        pull.Value = true;
        sim.Tick();
        Assert.False(estop.Ok.Value);
        Assert.False(key.Ok.Value);

        press.Value = false;
        pull.Value = false;
        sim.Tick();
        Assert.Equal(
            ["ESTOP_PRESSED", "PULLKEY_PULLED", "ESTOP_RELEASED", "PULLKEY_RESET"],
            sim.Events.Records.Select(r => r.Code));
    }

    [Fact]
    public void WiringOpenFailsSafeAndAWeldedContactFailsDangerous()
    {
        var estop = new EStop("ES1");
        var press = new Switch("Press");
        press.Out.ConnectTo(estop.Actuated);
        Simulation sim = new SimulationBuilder(Options()).Add(estop).Add(press).Build();

        sim.InjectFaultIn(TimeSpan.Zero, "ES1", SafetySwitch.WiringOpen);
        sim.Tick();
        Assert.False(estop.Ok.Value);

        sim.ClearFaultIn(TimeSpan.Zero, "ES1", SafetySwitch.WiringOpen);
        sim.InjectFaultIn(TimeSpan.Zero, "ES1", SafetySwitch.ContactWelded);
        press.Value = true;
        sim.Tick();
        Assert.True(estop.Ok.Value);
    }

    [Fact]
    public void TheRelayLatchesOnAnyOpenChannelAndResetsOnlyWhenHealthyAndReset()
    {
        var relay = new SafetyRelay("SR", channels: 2);
        var ch1 = new Switch("C1", true);
        var ch2 = new Switch("C2", true);
        var reset = new Switch("Reset");
        ch1.Out.ConnectTo(relay.Channel(1));
        ch2.Out.ConnectTo(relay.Channel(2));
        reset.Out.ConnectTo(relay.Reset);
        Simulation sim = new SimulationBuilder(Options()).Add(relay).Add(ch1).Add(ch2).Add(reset).Build();

        sim.Tick();
        Assert.False(relay.Ok.Value);          // needs a reset after power-up, like a real relay
        reset.Value = true;
        sim.Tick();
        Assert.True(relay.Ok.Value);
        reset.Value = false;
        sim.Tick();

        ch2.Value = false;
        sim.Tick();
        Assert.False(relay.Ok.Value);
        ch2.Value = true;
        sim.RunFor(TimeSpan.FromMilliseconds(50));
        Assert.False(relay.Ok.Value);          // latched

        reset.Value = true;
        sim.Tick();
        Assert.True(relay.Ok.Value);
        reset.Value = false;
        sim.Tick();
        Assert.True(relay.Ok.Value);           // reset is edge-triggered; holding it is not required

        ch1.Value = false;
        reset.Value = true;                    // held reset must not override an open channel
        sim.RunFor(TimeSpan.FromMilliseconds(30));
        Assert.False(relay.Ok.Value);

        var trips = sim.Events.Records.Where(r => r.Code == "SAFETY_TRIP").ToList();
        Assert.Equal(2, trips.Count);
        Assert.Contains("Channel2", trips[0].Message);
        Assert.Contains("Channel1", trips[1].Message);
        Assert.Equal(2, sim.Events.Records.Count(r => r.Code == "SAFETY_RESET"));
    }

    [Fact]
    public void RelayFaults()
    {
        var relay = new SafetyRelay("SR", 1);
        var ch1 = new Switch("C1", true);
        var reset = new Switch("Reset", true);
        ch1.Out.ConnectTo(relay.Channel(1));
        reset.Out.ConnectTo(relay.Reset);
        Simulation sim = new SimulationBuilder(Options()).Add(relay).Add(ch1).Add(reset).Build();
        sim.Tick();
        Assert.True(relay.Ok.Value);

        sim.InjectFaultIn(TimeSpan.Zero, "SR", SafetyRelay.CoilFailure);
        sim.Tick();
        Assert.False(relay.Ok.Value);

        sim.ClearFaultIn(TimeSpan.Zero, "SR", SafetyRelay.CoilFailure);
        sim.InjectFaultIn(TimeSpan.Zero, "SR", SafetyRelay.StuckEnergised);
        ch1.Value = false;
        sim.Tick();
        Assert.True(relay.Ok.Value);
    }

    [Fact]
    public void RelayRejectsBadChannelCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SafetyRelay("SR", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SafetyRelay("SR", 2).Channel(3));
    }
}
