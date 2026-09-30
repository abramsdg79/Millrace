using Dse.Components.Flow;
using Dse.Components.Tests.Fakes;
using Dse.Components.Transforms;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

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

    private static Plant Build(int batchSize, IHoldCondition hold, MaterialType? output = null, double yield = 1.0, IReadOnlyList<IMaterialTransform>? transforms = null, double interval = 1.0)
    {
        var source = new ItemSource("Billets", Billet, 20.0, interval, new MaterialProperties(7800.0, 0.0, 25.0));
        var unit = new ItemProcessUnit("Furnace", batchSize, hold, output, yield, transforms);
        var sink = new ItemSink("Out");
        var zone = new Setpoint("Zone", 1200.0);
        source.Out.ConnectTo(unit.In);
        unit.Out.ConnectTo(sink.In);
        zone.Out.ConnectTo(unit.AmbientTemperature);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(unit).Add(source).Add(zone).Build();
        return new Plant(sim, source, unit, sink, zone);
    }

    private static IEnumerable<string> Phases(Simulation sim) =>
        sim.Events.Records.Where(r => r.Source == "Furnace").Select(r => r.Code);

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
}
