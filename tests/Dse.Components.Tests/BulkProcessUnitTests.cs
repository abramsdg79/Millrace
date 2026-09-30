using Dse.Components.Flow;
using Dse.Components.Tests.Fakes;
using Dse.Components.Transforms;
using Dse.Core;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Logging;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class BulkProcessUnitTests
{
    private static readonly MaterialType Flour = new("Flour", PayloadKind.Bulk);
    private static readonly MaterialType Water = new("Water", PayloadKind.Bulk);
    private static readonly MaterialType Dough = new("Dough", PayloadKind.Bulk);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private sealed record Plant(Simulation Sim, BulkSource Flour, BulkSource Water, BulkProcessUnit Mixer, BulkSink Out, Setpoint Ambient);

    private static Plant Build(IHoldCondition hold, double yield = 1.0, IReadOnlyList<IMaterialTransform>? transforms = null)
    {
        var flour = new BulkSource("FlourFeed", Flour, 4.0, new MaterialProperties(600.0, 0.12, 20.0));
        var water = new BulkSource("WaterFeed", Water, 2.0, new MaterialProperties(1000.0, 1.0, 10.0));
        var mixer = new BulkProcessUnit(
            "Mixer",
            [new RecipeLine("Flour", Flour, 6.0), new RecipeLine("Water", Water, 4.0)],
            hold,
            Dough,
            yield,
            transforms);
        var sink = new BulkSink("Out");
        var ambient = new Setpoint("Ambient", 30.0);
        flour.Out.ConnectTo(mixer.Inlet("Flour"));
        water.Out.ConnectTo(mixer.Inlet("Water"));
        mixer.Out.ConnectTo(sink.In);
        ambient.Out.ConnectTo(mixer.AmbientTemperature);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(mixer).Add(flour).Add(water).Add(ambient).Build();
        return new Plant(sim, flour, water, mixer, sink, ambient);
    }

    private static IEnumerable<string> Phases(Simulation sim) =>
        sim.Events.Records.Where(r => r.Source == "Mixer").Select(r => r.Code);

    [Fact]
    public void FillsByRecipeMixesHoldsAndDischargesABlendedBatch()
    {
        Plant plant = Build(Hold.ForSeconds(3.0));

        plant.Sim.RunFor(TimeSpan.FromSeconds(4));    // flour 2 kg/tick needs 3 ticks, water 1 kg/tick needs 4
        Assert.Equal(ProcessPhase.Processing, plant.Mixer.Phase.Value);
        Assert.Equal(10.0, plant.Mixer.BatchMass.Value, 9);
        Assert.Equal(["FILLING", "PROCESSING"], Phases(plant.Sim));

        plant.Sim.RunFor(TimeSpan.FromSeconds(4));
        Assert.Equal(10.0, plant.Out.MassDestroyed, 9);
        Assert.Same(Dough, plant.Out.LastType);
        Assert.Equal(0.6 * 0.12 + 0.4 * 1.0, plant.Out.LastProperties.Moisture, 9);
        Assert.Equal(0.6 * 20.0 + 0.4 * 10.0, plant.Out.LastProperties.Temperature, 9);
        Assert.Equal(["FILLING", "PROCESSING", "DISCHARGING", "IDLE", "FILLING"], Phases(plant.Sim).Take(5));
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void OverSuppliedLinesBackUpDuringProcessing()
    {
        Plant plant = Build(Hold.ForSeconds(10.0));

        plant.Sim.RunFor(TimeSpan.FromSeconds(8));

        Assert.Equal(ProcessPhase.Processing, plant.Mixer.Phase.Value);
        Assert.True(plant.Flour.MassHeld > 0.0);
        Assert.True(plant.Water.MassHeld > 0.0);
        Assert.Equal(10.0, plant.Mixer.MassHeld, 9);
    }

    [Fact]
    public void APropertyHoldReleasesWhenTheBatchGetsThere()
    {
        Plant plant = Build(Hold.TemperatureAtLeast(29.0), transforms: [new ThermalTransfer(2.0)]);

        plant.Sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.True(plant.Out.MassDestroyed >= 10.0 - 1e-9);
        Assert.True(plant.Out.LastProperties.Temperature >= 29.0);
        Assert.Contains("DISCHARGING", Phases(plant.Sim));
    }

    [Fact]
    public void YieldLossIsBookedAsADeclaredLoss()
    {
        // Hold.ForSeconds(1.0) is a fast cycle (2 ticks at dt 0.5s): the unlimited-
        // capacity sink drains a batch in one tick and the backed-up sources refill
        // the recipe instantly, so three Release()s (each booking 1 kg loss on a
        // 10 kg batch) happen within 8 s — at ticks 7, 11 and 15 — but only the
        // first two batches finish transferring to the sink by the last tick, so
        // Out sees 18 kg while the unit's own loss ledger already shows 3 kg.
        // Telemetry is written in Evaluate, one phase before Advance runs, so it
        // still shows the 2 kg booked before the third Release. Measured via the
        // event log; the third batch's 9 kg sits mid-discharge, which is why the
        // conservation audit still nets to zero.
        Plant plant = Build(Hold.ForSeconds(1.0), yield: 0.9);

        plant.Sim.RunFor(TimeSpan.FromSeconds(8));

        Assert.Equal(18.0, plant.Out.MassDestroyed, 9);
        Assert.Equal(3.0, plant.Mixer.MassDestroyed, 9);
        Assert.Equal(2.0, plant.Sim.Telemetry.Read("Mixer.Lost"), 9);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void FaultsJamTheDischargeAndAddLoss()
    {
        Plant plant = Build(Hold.ForSeconds(1.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Mixer", BulkProcessUnit.DischargeJam);
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Mixer", BulkProcessUnit.YieldLoss, new FaultArguments(new FaultArgument("fraction", 0.2)));

        plant.Sim.RunFor(TimeSpan.FromSeconds(8));
        Assert.Equal(ProcessPhase.Discharging, plant.Mixer.Phase.Value);
        Assert.Equal(0.0, plant.Out.MassDestroyed, 9);
        Assert.Equal(2.0, plant.Mixer.MassDestroyed, 9);

        plant.Sim.ClearFaultIn(TimeSpan.Zero, "Mixer", BulkProcessUnit.DischargeJam);
        plant.Sim.RunFor(TimeSpan.FromSeconds(2));
        Assert.Equal(8.0, plant.Out.MassDestroyed, 9);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void HoldConditionsCompose()
    {
        var props = new MaterialProperties(1.0, 0.1, 50.0);
        IHoldCondition both = Hold.All(Hold.ForSeconds(5.0), Hold.TemperatureAtLeast(40.0));
        Assert.False(both.IsSatisfied(4.0, in props, ReadOnlySpan<double>.Empty));
        Assert.True(both.IsSatisfied(5.0, in props, ReadOnlySpan<double>.Empty));
        Assert.False(Hold.TemperatureAtMost(40.0).IsSatisfied(99.0, in props, ReadOnlySpan<double>.Empty));

        var type = new MaterialType("T", PayloadKind.Discrete, "Soak");
        IHoldCondition soak = Hold.StateAtLeast(type, "Soak", 2.0);
        Assert.False(soak.IsSatisfied(0.0, in props, [1.0]));
        Assert.True(soak.IsSatisfied(0.0, in props, [2.0]));
        Assert.False(soak.IsSatisfied(0.0, in props, ReadOnlySpan<double>.Empty));
    }

    [Fact]
    public void DischargingProgressTracksTheStartingBatchNotTheRecipeTarget()
    {
        // Drive the unit directly (bypassing Simulation/FlowGraph) so the exact
        // instant the batch empties can be observed: with an unlimited-capacity
        // sink a full batch always transfers in the same tick that Advance also
        // sees it empty and moves on to Idle, so a plant-level RunFor can never
        // catch Progress mid-Discharging — Evaluate always runs before that
        // tick's transfer+Advance, and by the next tick's Evaluate the phase has
        // already moved on. This is exactly the state a discharge fault (or any
        // consumer that simply hasn't withdrawn yet) would leave the unit in.
        var unit = new BulkProcessUnit(
            "Mixer",
            [new RecipeLine("Flour", Flour, 6.0), new RecipeLine("Water", Water, 4.0)],
            Hold.ForSeconds(1.0),
            Dough,
            yield: 0.9);

        unit.Deposit(unit.Inlet("Flour"), BulkLot.Of(Flour, 6.0, new MaterialProperties(600.0, 0.12, 20.0)));
        unit.Deposit(unit.Inlet("Water"), BulkLot.Of(Water, 4.0, new MaterialProperties(1000.0, 1.0, 10.0)));

        var log = new EventLog();
        var ctx = new TickContext(0, 1.0, DateTimeOffset.UnixEpoch, log);
        unit.Advance(ctx);   // Idle -> Filling -> Processing (recipe already complete)
        Assert.Equal(ProcessPhase.Processing, unit.CurrentPhase);

        unit.Advance(ctx);   // elapsed 0 -> 1.0 s; the 1 s hold releases: yield 0.9 leaves 9 kg
        Assert.Equal(ProcessPhase.Discharging, unit.CurrentPhase);

        unit.Evaluate(ctx);
        Assert.Equal(0.0, unit.Progress.Value, 9);

        unit.Withdraw(unit.Out, unit.MassHeld);   // a consumer takes the whole batch
        unit.Evaluate(ctx);
        Assert.Equal(1.0, unit.Progress.Value, 9);
    }

    [Fact]
    public void TryObserveReportsEmptyThenBlendedLinesThenTheBatch()
    {
        var unit = new BulkProcessUnit(
            "Mixer",
            [new RecipeLine("Flour", Flour, 6.0), new RecipeLine("Water", Water, 4.0)],
            Hold.ForSeconds(1.0),
            Dough);

        Assert.False(unit.TryObserve(0.0, 0.0, out _));

        unit.Deposit(unit.Inlet("Flour"), BulkLot.Of(Flour, 3.0, new MaterialProperties(600.0, 0.12, 20.0)));
        unit.Deposit(unit.Inlet("Water"), BulkLot.Of(Water, 1.0, new MaterialProperties(1000.0, 1.0, 10.0)));

        var log = new EventLog();
        var ctx = new TickContext(0, 1.0, DateTimeOffset.UnixEpoch, log);
        unit.Advance(ctx);   // Idle -> Filling; recipe not yet complete (3/6 flour, 1/4 water)
        Assert.Equal(ProcessPhase.Filling, unit.CurrentPhase);

        Assert.True(unit.TryObserve(0.0, 0.0, out MaterialObservation filling));
        Assert.Equal(4.0, filling.Mass, 9);
        Assert.Equal(0.0, filling.LinearDensity, 9);
        Assert.Equal(0L, filling.ItemId);
        Assert.Equal((3.0 * 20.0 + 1.0 * 10.0) / 4.0, filling.Properties.Temperature, 9);

        unit.Deposit(unit.Inlet("Flour"), BulkLot.Of(Flour, 3.0, new MaterialProperties(600.0, 0.12, 20.0)));
        unit.Deposit(unit.Inlet("Water"), BulkLot.Of(Water, 3.0, new MaterialProperties(1000.0, 1.0, 10.0)));
        unit.Advance(ctx);   // Filling -> Processing; recipe now complete (6 flour, 4 water)
        Assert.Equal(ProcessPhase.Processing, unit.CurrentPhase);

        Assert.True(unit.TryObserve(0.0, 0.0, out MaterialObservation processing));
        Assert.Equal(10.0, processing.Mass, 9);
        Assert.Equal(0.0, processing.LinearDensity, 9);
        Assert.Equal(0L, processing.ItemId);
        Assert.Equal((0.6 * 20.0) + (0.4 * 10.0), processing.Properties.Temperature, 9);
    }

    [Fact]
    public void RejectsBadRecipes()
    {
        Assert.Throws<ArgumentException>(() => new BulkProcessUnit("M", [], Hold.ForSeconds(1.0), Dough));
        Assert.Throws<ArgumentException>(() => new BulkProcessUnit(
            "M", [new RecipeLine("A", Flour, 1.0), new RecipeLine("A", Water, 1.0)], Hold.ForSeconds(1.0), Dough));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkProcessUnit(
            "M", [new RecipeLine("A", Flour, 1.0)], Hold.ForSeconds(1.0), Dough, yield: 1.5));
    }

    [Fact]
    public void TheHoldSatisfiedMessagePrintsSecondsToTwoDecimalsAndMassToOne()
    {
        Plant plant = Build(Hold.ForSeconds(1.0), yield: 0.9);

        plant.Sim.RunFor(TimeSpan.FromSeconds(4));

        Assert.Equal(
            "Hold satisfied after 1.00 s; discharging 9.0 kg of Dough.",
            plant.Sim.Events.Records.First(r => r.Source == "Mixer" && r.Code == "DISCHARGING").Message);
    }
}
