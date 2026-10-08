using Millrace.Components.Flow;
using Millrace.Components.Instruments;
using Millrace.Control;
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Time;

namespace Millrace.Configuration.Tests;

/// <summary>
/// R168: a reject gate's dwell must cover the PLC chain that decides it — the
/// pyrometer's tick, an alarm scan, a coil scan and the tick the write lands.
/// Built in code here because this project is the one that sees both
/// Millrace.Components and Millrace.Control.
/// </summary>
public class RejectChainTests
{
    [Theory]
    [InlineData(100, 100, 0.3, 8)]   // scans every tick: the write lands 4 ticks after arrival, the item may leave after 3 + 1
    [InlineData(100, 100, 0.2, 0)]   // one tick short: every hot item passes
    [InlineData(200, 100, 0.4, 8)]   // an alarm scanning every other tick needs one more tick of dwell
    [InlineData(200, 100, 0.3, 4)]   // the alarm-period term: the dwell that falls short rejects only some items
    [InlineData(100, 100, 0.25, 8)]  // a dwell that is not a multiple of the step still covers the chain
    public void AHotItemIsRejectedOnlyWhenTheDwellCoversTheChain(int alarmScanMs, int coilScanMs, double dwellSeconds, int rejected)
    {
        var options = new SimulationOptions
        {
            Seed = 1UL,
            StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
            TimeStep = TimeSpan.FromMilliseconds(100),
        };
        var billet = new MaterialType("Billet", PayloadKind.Discrete);
        var source = new ItemSource("SRC", billet, 10.0, 7.3, new MaterialProperties(7800.0, 0.0, 1000.0));
        var gate = new RejectGate("GATE", dwellSeconds);
        var good = new ItemSink("GOOD");
        var scrap = new ItemSink("SCRAP");
        source.Out.ConnectTo(gate.In);
        gate.Out.ConnectTo(good.In);
        gate.RejectOut.ConnectTo(scrap.In);
        var pyrometer = new Pyrometer("TT", gate, 0.0, 0.0, new InstrumentSpec("degC", 0.0, 1500.0));
        Simulation sim = new SimulationBuilder(options)
            .Add(good).Add(scrap).Add(gate).Add(source).Add(pyrometer)
            .AddScanBlock(new Alarm(
                "ALM01", "TT.Value", [new AlarmLimit(AlarmLimitKind.HiHi, 900.0, 0.0, TimeSpan.Zero)], TimeSpan.FromMilliseconds(alarmScanMs)))
            .AddScanBlock(new Coil("COIL01", new Condition("ALM01.HiHi.Active", true), "GATE.Reject", TimeSpan.FromMilliseconds(coilScanMs)), ["GATE.Reject"])
            .Build();

        sim.RunFor(TimeSpan.FromSeconds(60));   // eight 1000 °C billets, 7.3 s apart

        Assert.Equal(rejected * 10.0, scrap.MassReceived);
        Assert.Equal((8 - rejected) * 10.0, good.MassReceived);
    }
}
