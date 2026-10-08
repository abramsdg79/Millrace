using Millrace.Components.Flow;
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Components.Tests;

public class FormerTests
{
    private static readonly MaterialType Dough = new("Dough", PayloadKind.Bulk);
    private static readonly MaterialType Piece = new("DoughPiece", PayloadKind.Discrete, "ProofTime");

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private static (Simulation Sim, BulkSource Feed, Former Divider, ItemSink Tray) Build(double feedRate = 2.0, int trayCapacity = int.MaxValue)
    {
        var feed = new BulkSource("Feed", Dough, feedRate, new MaterialProperties(1050.0, 0.45, 26.0));
        var divider = new Former("Divider", Dough, Piece, pieceMassKg: 0.8, cycleSeconds: 1.0, hopperCapacityKg: 5.0);
        var tray = new ItemSink("Tray", trayCapacity);
        feed.Out.ConnectTo(divider.In);
        divider.Out.ConnectTo(tray.In);
        Simulation sim = new SimulationBuilder(Options()).Add(tray).Add(divider).Add(feed).Build();
        return (sim, feed, divider, tray);
    }

    [Fact]
    public void CutsOnePieceEveryCycleCarryingTheHoppersProperties()
    {
        var plant = Build();

        plant.Sim.RunFor(TimeSpan.FromSeconds(20));

        Assert.True(plant.Tray.LastItem is not null);
        Assert.Equal(Piece, plant.Tray.LastItem!.Type);
        Assert.Equal(0.8, plant.Tray.LastItem.Mass, 9);
        Assert.Equal(26.0, plant.Tray.LastItem.Properties.Temperature, 9);
        Assert.Single(plant.Tray.LastItem.State);
        Assert.InRange(plant.Tray.MassReceived, 0.8 * 16, 0.8 * 20);   // one per second (every second tick) minus fill and hand-off
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void WaitsForEnoughDoughAndFillsTheHopperWhenStarvedOfCycles()
    {
        var plant = Build(feedRate: 0.2);   // 0.1 kg per tick: a piece every 4 s, on the next cycle boundary

        plant.Sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.InRange(plant.Sim.Items.Issued, 6L, 7L);   // 6 kg fed; the last piece may be one tick short
        Assert.True(plant.Divider.Hopper.Mass < 0.9);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ABlockedOutletFillsTheQueueThenTheHopperThenTheFeed()
    {
        var plant = Build(trayCapacity: 0);

        plant.Sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.Equal(1.0, plant.Divider.HopperLevel.Value, 9);
        Assert.True(plant.Divider.Queued.Value > 0);
        Assert.True(plant.Feed.MassHeld > 0.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void AJamStopsFormingUntilCleared()
    {
        var plant = Build();
        plant.Sim.InjectFaultAt(TimeSpan.FromSeconds(5), "Divider", Former.Jam);
        plant.Sim.ClearFaultAt(TimeSpan.FromSeconds(15), "Divider", Former.Jam);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));
        long duringJam = plant.Sim.Items.Issued;
        plant.Sim.RunFor(TimeSpan.FromSeconds(5));
        Assert.Equal(duringJam, plant.Sim.Items.Issued);

        plant.Sim.RunFor(TimeSpan.FromSeconds(5));
        Assert.True(plant.Sim.Items.Issued > duringJam);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void RejectsMismatchedKinds()
    {
        Assert.Throws<ArgumentException>(() => new Former("F", Piece, Piece, 1.0, 1.0, 5.0));
        Assert.Throws<ArgumentException>(() => new Former("F", Dough, Dough, 1.0, 1.0, 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Former("F", Dough, Piece, 6.0, 1.0, 5.0));
    }
}
