using Dse.Components.Flow;
using Dse.Components.Tests.Fakes;
using Dse.Components.Transforms;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Logging;
using Dse.Core.Time;
using Dse.Io;
using Xunit;

namespace Dse.Components.Tests;

/// <summary>
/// Spec 6b.1 criterion 6, the physical half of the wheel line's causal chain:
/// billets → furnace (heat while held, zone above the discharge target) →
/// reject gate → belt → press. A slow press queues blanks on the belt, the
/// gate cannot pass its billet, the furnace cannot discharge, and the billet
/// it holds over-soaks toward the zone temperature.
/// </summary>
public class RejectLineTests
{
    /// <summary>The over-soak limit: well above the 1100 °C discharge target, well below the 1250 °C zone.</summary>
    private const double OverSoak = 1150.0;

    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);
    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private sealed record Line(
        Simulation Sim, ItemProcessUnit Furnace, RejectGate Gate, DiscreteBelt Belt, ItemProcessUnit Press, ItemSink Wheels, ItemSink Scrap);

    /// <summary>
    /// One 20 kg billet at 25 °C every 45 s; a one-billet furnace in a 1250 °C zone
    /// (20 s time constant) that discharges at 1100 °C, about 42 s in; a 2 s
    /// measuring station; a 3 m belt at 0.5 m/s holding at most four blanks 1 m
    /// apart; a press that forms a wheel in 15 s.
    /// </summary>
    private static Line Build()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 45.0, new MaterialProperties(7800.0, 0.0, 25.0));
        var furnace = new ItemProcessUnit(
            "Furnace", 1, Hold.TemperatureAtLeast(1100.0), transforms: [new ThermalTransfer(20.0)], heatWhileHeld: true);
        var zone = new Setpoint("Zone", 1250.0);
        var gate = new RejectGate("Gate", 2.0);
        var belt = new DiscreteBelt("Belt", length: 3.0, maxSpeed: 1.0, minSpacing: 1.0);
        var speed = new Setpoint("BeltSpeed", 0.5);
        var press = new ItemProcessUnit("Press", 1, Hold.ForSeconds(15.0), Wheel, yield: 0.95);
        var wheels = new ItemSink("Wheels");
        var scrap = new ItemSink("Scrap");
        source.Out.ConnectTo(furnace.In);
        furnace.Out.ConnectTo(gate.In);
        gate.Out.ConnectTo(belt.In);
        gate.RejectOut.ConnectTo(scrap.In);
        belt.Out.ConnectTo(press.In);
        press.Out.ConnectTo(wheels.In);
        zone.Out.ConnectTo(furnace.AmbientTemperature);
        speed.Out.ConnectTo(belt.Speed);
        Simulation sim = new SimulationBuilder(Options())
            .Add(wheels).Add(scrap).Add(press).Add(belt).Add(gate).Add(furnace).Add(source).Add(zone).Add(speed)
            .Build();
        return new Line(sim, furnace, gate, belt, press, wheels, scrap);
    }

    [Fact]
    public void UnblockedEveryBilletLeavesTheFurnaceAtItsDischargeTarget()
    {
        Line line = Build();
        double hottest = 0.0;
        int longestHold = 0;
        int hold = 0;
        int mostOnBelt = 0;

        for (int tick = 0; tick < 1200; tick++)   // 600 s
        {
            line.Sim.Tick();
            hottest = Math.Max(hottest, line.Gate.Item?.Properties.Temperature ?? 0.0);
            hold = line.Furnace.CurrentPhase == ProcessPhase.Discharging ? hold + 1 : 0;
            longestHold = Math.Max(longestHold, hold);
            mostOnBelt = Math.Max(mostOnBelt, line.Belt.Items.Count);
        }

        // Measured: every billet reaches the station at 1100.197 °C, the furnace is
        // in Discharging for one tick per billet, and the belt never holds two.
        Assert.InRange(hottest, 1100.0, 1101.0);
        Assert.Equal(1, longestHold);
        Assert.Equal(1, mostOnBelt);
        Assert.True(line.Wheels.Count.Value >= 10);
        Assert.Null(line.Scrap.LastItem);
        Assert.Equal(0.0, line.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ASlowPressBacksUpTheLineOverSoaksTheHeldBilletAndTheGateRejectsIt()
    {
        Line line = Build();
        line.Sim.InjectFaultAt(
            TimeSpan.Zero, "Press", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", 0.9)));
        line.Sim.WriteAt(TimeSpan.FromSeconds(560), "Gate.Reject", TagValue.Bool(true));
        line.Sim.WriteAt(TimeSpan.FromSeconds(570), "Gate.Reject", TagValue.Bool(false));

        // The press now takes 150 s a wheel. By 446.5 s the belt holds four blanks,
        // billet 8 waits on the station and billet 9, at its target, cannot leave.
        line.Sim.RunFor(TimeSpan.FromSeconds(500));   // ticks 0-999

        Assert.Equal(4, line.Belt.Items.Count);
        Assert.Equal(8L, line.Gate.Item!.Id);
        Assert.Equal(ProcessPhase.Discharging, line.Furnace.CurrentPhase);
        ItemInstance held = Assert.Single(line.Furnace.Items);
        Assert.Equal(9L, held.Id);
        Assert.InRange(held.Properties.Temperature, 1235.0, 1245.0);   // measured 1239.77 °C
        Assert.True(held.Properties.Temperature > OverSoak);

        // Billet 9 reaches the station at 550.5 s, over-soaked; the write that lands
        // at 560 s sends it to the reject outlet on that tick.
        line.Sim.RunFor(TimeSpan.FromSeconds(100));   // ticks 1000-1199

        Assert.Equal(9L, line.Scrap.LastItem!.Id);
        Assert.InRange(line.Scrap.LastItem.Properties.Temperature, 1245.0, 1250.0);   // measured 1249.21 °C
        Assert.Equal(20.0, line.Scrap.MassReceived);
        SimEventRecord rejected = Assert.Single(line.Sim.Events.Records, r => r.Code == "REJECTED");
        Assert.Equal((1120L, "Gate", "Item 9 rejected."), (rejected.Tick, rejected.Source, rejected.Message));
        Assert.Equal(10L, line.Gate.Item!.Id);   // the next billet, at its target, waits for the belt
        Assert.Equal(0.0, line.Sim.MassBalance.Drift, 9);
    }
}
