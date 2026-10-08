using Millrace.Components.Flow;
using Millrace.Components.Tests.Fakes;
using Millrace.Components.Transforms;
using Millrace.Core;
using Millrace.Core.Faults;
using Millrace.Core.Flow;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Components.Tests;

public class ItemProcessUnitTests
{
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete, "SoakTime");
    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private sealed record Plant(Simulation Sim, ItemSource Source, ItemProcessUnit Unit, ItemSink Sink, Setpoint Zone);

    private static Plant Build(
        int batchSize,
        IHoldCondition hold,
        MaterialType? output = null,
        double yield = 1.0,
        IReadOnlyList<IMaterialTransform>? transforms = null,
        double interval = 1.0,
        bool heatWhileHeld = false,
        int sinkCapacity = int.MaxValue)
    {
        var source = new ItemSource("Billets", Billet, 20.0, interval, new MaterialProperties(7800.0, 0.0, 25.0));
        var unit = new ItemProcessUnit("Furnace", batchSize, hold, output, yield, transforms, heatWhileHeld);
        var sink = new ItemSink("Out", sinkCapacity);
        var zone = new Setpoint("Zone", 1200.0);
        source.Out.ConnectTo(unit.In);
        unit.Out.ConnectTo(sink.In);
        zone.Out.ConnectTo(unit.AmbientTemperature);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(unit).Add(source).Add(zone).Build();
        return new Plant(sim, source, unit, sink, zone);
    }

    private static IEnumerable<string> Phases(Simulation sim) =>
        sim.Events.Records.Where(r => r.Source == "Furnace").Select(r => r.Code);

    /// <summary>A billet that entered at 25 °C after <paramref name="ticks"/> half-second steps toward a 1200 °C zone with a 10 s time constant.</summary>
    private static double Heated(int ticks) => 1200.0 - (1175.0 * Math.Pow(0.95, ticks));

    /// <summary>The tick the first DISCHARGING was logged on.</summary>
    private static long DischargeTick(Simulation sim) =>
        sim.Events.Records.First(r => r.Source == "Furnace" && r.Code == "DISCHARGING").Tick;

    [Fact]
    public void FillsToTheBatchSizeHoldsAndDischargesInOrder()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));

        // Items land on ticks 2, 4, 6; PROCESSING on tick 7; the 2 s hold releases
        // on tick 11; all three leave on tick 12 and IDLE is logged the same tick.
        plant.Sim.RunFor(TimeSpan.FromSeconds(7));

        Assert.Equal(3L, plant.Sink.Count.Value);
        Assert.Equal(3L, plant.Sink.LastItem!.Id);
        Assert.Equal(["FILLING", "PROCESSING", "DISCHARGING", "IDLE"], Phases(plant.Sim).Take(4));
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ASoakConditionOnStateReleasesWhenEveryItemHasSoaked()
    {
        Plant plant = Build(
            batchSize: 2,
            Hold.StateAtLeast(Billet, "SoakTime", 3.0),
            transforms: [new ThermalTransfer(1.0), ResidenceAccumulator.For(Billet, "SoakTime", 1100.0)]);

        plant.Sim.RunFor(TimeSpan.FromSeconds(40));

        Assert.True(plant.Sink.Count.Value >= 2);
        Assert.True(plant.Sink.LastItem!.State[0] >= 3.0);
        Assert.True(plant.Sink.LastItem.Properties.Temperature > 1100.0);
    }

    [Fact]
    public void APressChangesTypeAndLosesFlash()
    {
        Plant plant = Build(batchSize: 1, Hold.ForSeconds(1.0), output: Wheel, yield: 0.95);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.True(plant.Sink.Count.Value >= 1);
        Assert.Same(Wheel, plant.Sink.LastItem!.Type);
        Assert.Equal(19.0, plant.Sink.LastItem.Mass, 9);
        Assert.Empty(plant.Sink.LastItem.State);
        // Loss is booked at release, one tick before discharge, so it may run one item ahead of the sink.
        Assert.InRange(plant.Unit.MassDestroyed, plant.Sink.Count.Value * 1.0, (plant.Sink.Count.Value + 2) * 1.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void AFullUnitBacksUpItsFeed()
    {
        Plant plant = Build(batchSize: 2, Hold.ForSeconds(30.0), interval: 0.5);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(ProcessPhase.Processing, plant.Unit.Phase.Value);
        Assert.Equal(2, plant.Unit.ItemCount.Value);
        Assert.True(plant.Source.Queued.Value > 5);
    }

    [Fact]
    public void ADischargeJamHoldsTheBatch()
    {
        Plant plant = Build(batchSize: 1, Hold.ForSeconds(1.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.DischargeJam);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(0L, plant.Sink.Count.Value);
        Assert.Equal(ProcessPhase.Discharging, plant.Unit.Phase.Value);

        plant.Sim.ClearFaultIn(TimeSpan.Zero, "Furnace", ItemProcessUnit.DischargeJam);
        plant.Sim.RunFor(TimeSpan.FromSeconds(2));
        Assert.True(plant.Sink.MassReceived > 0.0);
    }

    [Fact]
    public void ObservationShowsTheHeadItem()
    {
        Plant plant = Build(batchSize: 2, Hold.ForSeconds(30.0));
        plant.Sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.True(plant.Unit.TryObserve(0.0, 0.0, out MaterialObservation seen));
        Assert.Equal(1L, seen.ItemId);
        Assert.Equal(20.0, seen.Mass);
    }

    [Fact]
    public void RejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemProcessUnit("U", 0, Hold.ForSeconds(1.0)));
        Assert.Throws<ArgumentException>(() => new ItemProcessUnit("U", 1, Hold.ForSeconds(1.0), new MaterialType("B", PayloadKind.Bulk)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemProcessUnit("U", 1, Hold.ForSeconds(1.0), yield: 0.0));
    }

    [Fact]
    public void TheHoldSatisfiedMessagePrintsSecondsToTwoDecimals()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));

        plant.Sim.RunFor(TimeSpan.FromSeconds(7));

        Assert.Equal(
            "Hold satisfied after 2.00 s; discharging 3 items.",
            Assert.Single(plant.Sim.Events.Records, r => r.Source == "Furnace" && r.Code == "DISCHARGING").Message);
    }

    [Fact]
    public void WithHeatWhileHeldAJammedBatchHeatsOnEveryTickTheUnitHoldsIt()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0), transforms: [new ThermalTransfer(10.0)], heatWhileHeld: true);
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.DischargeJam);

        // Items land on ticks 2, 4, 6 and are heated from the next tick on, in
        // Idle, Filling, Processing (ticks 8-11) and Discharging alike: after
        // tick 19 they have had 17, 15 and 13 steps.
        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(ProcessPhase.Discharging, plant.Unit.CurrentPhase);
        Assert.Equal(new long[] { 1, 2, 3 }, plant.Unit.Items.Select(i => i.Id));
        Assert.Equal(Heated(17), plant.Unit.Items[0].Properties.Temperature, 9);
        Assert.Equal(Heated(15), plant.Unit.Items[1].Properties.Temperature, 9);
        Assert.Equal(Heated(13), plant.Unit.Items[2].Properties.Temperature, 9);
    }

    [Fact]
    public void WithHeatWhileHeldABatchBlockedDownstreamHeatsExactlyAsAJammedOne()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0), transforms: [new ThermalTransfer(10.0)], heatWhileHeld: true, sinkCapacity: 0);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(ProcessPhase.Discharging, plant.Unit.CurrentPhase);
        Assert.Equal(0L, plant.Sink.Count.Value);
        Assert.Equal(Heated(17), plant.Unit.Items[0].Properties.Temperature, 9);
        Assert.Equal(Heated(15), plant.Unit.Items[1].Properties.Temperature, 9);
        Assert.Equal(Heated(13), plant.Unit.Items[2].Properties.Temperature, 9);
    }

    [Fact]
    public void WithoutHeatWhileHeldAHeldBatchHeatsOnlyWhileProcessing()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0), transforms: [new ThermalTransfer(10.0)], sinkCapacity: 0);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(ProcessPhase.Discharging, plant.Unit.CurrentPhase);
        Assert.All(plant.Unit.Items, i => Assert.Equal(Heated(4), i.Properties.Temperature, 9));
    }

    [Theory]
    [InlineData(0.5, 15L, "Hold satisfied after 2.00 s of hold (4.00 s elapsed); discharging 3 items.")]
    [InlineData(0.75, 23L, "Hold satisfied after 2.00 s of hold (8.00 s elapsed); discharging 3 items.")]
    [InlineData(0.0, 11L, "Hold satisfied after 2.00 s; discharging 3 items.")]
    [InlineData(-1.0, 11L, "Hold satisfied after 2.00 s; discharging 3 items.")]
    public void ASlowCycleStretchesATimedHoldByOneOverOneMinusTheFraction(double fraction, long dischargeTick, string message)
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", fraction)));

        // PROCESSING is logged on tick 7; unslowed, the 2 s hold is four ticks.
        plant.Sim.RunFor(TimeSpan.FromSeconds(15));

        Assert.Equal(dischargeTick, DischargeTick(plant.Sim));
        Assert.Equal(message, plant.Sim.Events.Records.First(r => r.Source == "Furnace" && r.Code == "DISCHARGING").Message);
    }

    [Fact]
    public void ASlowCycleDefaultsToHalfRate()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(15L, DischargeTick(plant.Sim));
        Assert.Contains(plant.Sim.Events.Records, r => r.Source == "Furnace" && r.Message == "slow-cycle injected: fraction=0.5.");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void AtFullSlowCycleATimedHoldNeverCompletes(double fraction)
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", fraction)));

        plant.Sim.RunFor(TimeSpan.FromSeconds(60));

        Assert.Equal(ProcessPhase.Processing, plant.Unit.CurrentPhase);
        Assert.Equal(0.0, plant.Unit.Progress.Value);
        Assert.DoesNotContain(plant.Sim.Events.Records, r => r.Source == "Furnace" && r.Code == "DISCHARGING");
    }

    [Fact]
    public void ASlowCycleLeavesATemperatureHoldAlone()
    {
        IMaterialTransform[] heat = [new ThermalTransfer(10.0)];
        Plant plain = Build(batchSize: 3, Hold.TemperatureAtLeast(200.0), transforms: heat);
        Plant slowed = Build(batchSize: 3, Hold.TemperatureAtLeast(200.0), transforms: heat);
        slowed.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", 1.0)));

        plain.Sim.RunFor(TimeSpan.FromSeconds(10));
        slowed.Sim.RunFor(TimeSpan.FromSeconds(10));

        // 192.6 °C after three processing steps, 243.0 °C after four: released on tick 11 either way.
        Assert.Equal(11L, DischargeTick(plain.Sim));
        Assert.Equal(11L, DischargeTick(slowed.Sim));
    }

    [Fact]
    public void ClearingAFullSlowCycleResumesTheStoppedHold()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", 1.0)));
        plant.Sim.ClearFaultAt(TimeSpan.FromSeconds(6), "Furnace", ItemProcessUnit.SlowCycle);   // tick 12

        // Ticks 8-11 count nothing; ticks 12-15 count the whole 2 s.
        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(15L, DischargeTick(plant.Sim));
    }

    [Fact]
    public void ClearingASlowCycleRestoresFullRateAndKeepsTheTimeAlreadyCounted()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);
        plant.Sim.ClearFaultAt(TimeSpan.FromSeconds(5), "Furnace", ItemProcessUnit.SlowCycle);

        // Ticks 8 and 9 count 0.25 s each, ticks 10 on count 0.5 s: 2 s after tick 12.
        // Unslowed it would be tick 11, always slowed tick 15, restarted on clear tick 13.
        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(12L, DischargeTick(plant.Sim));
    }

    [Fact]
    public void ProgressFollowsTheSlowedHoldTimer()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);
        plant.Sim.RunFor(TimeSpan.FromSeconds(4));   // ticks 0-7: PROCESSING on tick 7

        var progress = new List<double>();
        for (int i = 0; i < 8; i++)
        {
            plant.Sim.Tick();                          // ticks 8-15; each publishes the timer as of the tick before
            progress.Add(plant.Unit.Progress.Value);
        }

        Assert.Equal([0.0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875], progress);
        Assert.Equal(ProcessPhase.Discharging, plant.Unit.CurrentPhase);
    }

    [Fact]
    public void ASlowCycleAndADischargeJamAreIndependent()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.DischargeJam);
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(15L, DischargeTick(plant.Sim));
        Assert.Equal(0L, plant.Sink.Count.Value);

        plant.Sim.ClearFaultIn(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);
        plant.Sim.RunFor(TimeSpan.FromSeconds(2));
        Assert.Equal(ProcessPhase.Discharging, plant.Unit.CurrentPhase);
        Assert.Equal(0.0, plant.Sink.MassReceived);

        plant.Sim.ClearFaultIn(TimeSpan.Zero, "Furnace", ItemProcessUnit.DischargeJam);
        plant.Sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(60.0, plant.Sink.MassReceived);
    }

    /// <summary>The first DISCHARGING message.</summary>
    private static string HoldMessage(Simulation sim) =>
        sim.Events.Records.First(r => r.Source == "Furnace" && r.Code == "DISCHARGING").Message;

    [Fact]
    public void AClearedSlowCycleLeavesTheHoldTimeAndTheWallTimeApart()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);
        plant.Sim.ClearFaultAt(TimeSpan.FromSeconds(5), "Furnace", ItemProcessUnit.SlowCycle);

        // Ticks 8 and 9 count 0.25 s each, ticks 10-12 0.5 s: 2 s of hold in 2.5 s.
        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal("Hold satisfied after 2.00 s of hold (2.50 s elapsed); discharging 3 items.", HoldMessage(plant.Sim));
    }

    [Fact]
    public void ASlowCycleClearedBeforeProcessingLeavesTheMessageAsItWas()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);
        plant.Sim.ClearFaultAt(TimeSpan.FromSeconds(3), "Furnace", ItemProcessUnit.SlowCycle);   // tick 6, before PROCESSING on tick 7

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(11L, DischargeTick(plant.Sim));
        Assert.Equal("Hold satisfied after 2.00 s; discharging 3 items.", HoldMessage(plant.Sim));
    }

    [Fact]
    public void AHoldSlowedTooLittleToShowAtTwoDecimalsPrintsOneTime()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", 0.001)));

        // 0.4995 s a tick: 1.998 s after four ticks, 2.4975 s after five, in 2.5 s of wall time.
        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(12L, DischargeTick(plant.Sim));
        Assert.Equal("Hold satisfied after 2.50 s; discharging 3 items.", HoldMessage(plant.Sim));
    }

    [Fact]
    public void ATemperatureHoldUnderAFullSlowCycleGivesTheWallTime()
    {
        Plant plant = Build(batchSize: 3, Hold.TemperatureAtLeast(200.0), transforms: [new ThermalTransfer(10.0)]);
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", 1.0)));

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(11L, DischargeTick(plant.Sim));
        Assert.Equal("Hold satisfied after 0.00 s of hold (2.00 s elapsed); discharging 3 items.", HoldMessage(plant.Sim));
    }
}
