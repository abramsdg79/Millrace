using Dse.Components.Flow;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class TransferChuteTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private static (Simulation Sim, BulkSource Feed, TransferChute Chute, BulkSink Pile) Build(double sinkCapacity = double.PositiveInfinity)
    {
        var feed = new BulkSource("Feed", Ore, 2.0, new MaterialProperties(1600.0, 0.05, 15.0));
        var chute = new TransferChute("Chute", capacityKg: 3.0);
        var pile = new BulkSink("Pile", sinkCapacity);
        feed.Out.ConnectTo(chute.In);
        chute.Out.ConnectTo(pile.In);
        Simulation sim = new SimulationBuilder(Options()).Add(pile).Add(chute).Add(feed).Build();
        return (sim, feed, chute, pile);
    }

    [Fact]
    public void PassesMaterialThroughWithOneTickOfResidence()
    {
        var plant = Build();

        plant.Sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(8.0, plant.Pile.MassDestroyed, 9);      // created tick 0, chute tick 1, pile tick 2 …
        Assert.Equal(1.0, plant.Chute.MassHeld, 9);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void FillsToCapacityWhenBlockedDownstreamAndSaysSoOnce()
    {
        var plant = Build(sinkCapacity: 0.0);

        plant.Sim.RunFor(TimeSpan.FromSeconds(6));

        Assert.Equal(3.0, plant.Chute.MassHeld, 9);
        Assert.Equal(1.0, plant.Chute.Level.Value, 9);
        Assert.True(plant.Chute.Full.Value);
        Assert.Single(plant.Sim.Events.Records, r => r.Code == "FULL" && r.Source == "Chute");
        Assert.True(plant.Feed.MassHeld > 0.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ABlockageStopsDischargeAndClearingResumesIt()
    {
        var plant = Build();
        plant.Sim.RunFor(TimeSpan.FromSeconds(3));
        double sunkBefore = plant.Pile.MassDestroyed;

        plant.Sim.InjectFaultIn(TimeSpan.Zero, "Chute", TransferChute.Blockage);
        plant.Sim.RunFor(TimeSpan.FromSeconds(4));

        Assert.Equal(sunkBefore, plant.Pile.MassDestroyed, 9);
        Assert.Equal(3.0, plant.Chute.MassHeld, 9);
        Assert.Contains(plant.Sim.Events.Records, r => r.Code == "FULL");

        plant.Sim.ClearFaultIn(TimeSpan.Zero, "Chute", TransferChute.Blockage);
        plant.Sim.RunFor(TimeSpan.FromSeconds(3));

        Assert.True(plant.Pile.MassDestroyed > sunkBefore + 2.0);
        Assert.Contains(plant.Sim.Events.Records, r => r.Code == "CLEARED");
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ObservationReportsTheContents()
    {
        var plant = Build(sinkCapacity: 0.0);
        plant.Sim.RunFor(TimeSpan.FromSeconds(2));

        Assert.True(plant.Chute.TryObserve(0.0, 0.0, out MaterialObservation seen));
        Assert.Equal(plant.Chute.MassHeld, seen.Mass, 9);
        Assert.Equal(15.0, seen.Properties.Temperature);
        Assert.Equal(0L, seen.ItemId);

        var empty = new TransferChute("E", 1.0);
        Assert.False(empty.TryObserve(0.0, 0.0, out _));
    }
}
