using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Xunit;

namespace Dse.Core.Tests;

public class ObservationTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);

    [Fact]
    public void ABulkBeltReportsTheCellUnderThePosition()
    {
        var belt = new BulkBelt("CV", length: 2.0, cellSize: 0.5, maxSpeed: 1.0, maxLinearDensity: 10.0);
        belt.Initialize(TestContexts.Init(belt.Id, dt: 0.5));
        belt.Deposit(belt.In, BulkLot.Of(Ore, 2.0, new MaterialProperties(1600.0, 0.1, 30.0)));

        Assert.True(belt.TryObserve(0.25, 0.0, out MaterialObservation tail));
        Assert.Equal(2.0, tail.Mass);
        Assert.Equal(4.0, tail.LinearDensity);
        Assert.Equal(30.0, tail.Properties.Temperature);
        Assert.Equal(0L, tail.ItemId);

        Assert.False(belt.TryObserve(1.75, 0.0, out _));
    }

    [Fact]
    public void ADiscreteBeltReportsTheItemWithinTheWindow()
    {
        var belt = new DiscreteBelt("CV", length: 4.0, maxSpeed: 1.0);
        belt.Initialize(TestContexts.Init(belt.Id, dt: 0.5));
        var speed = new OutputPort<double>("Out", "SP");
        speed.ConnectTo(belt.Speed);
        speed.Value = 1.0;

        var first = new ItemInstance(1, Billet, 5.0, new MaterialProperties(7800.0, 0.0, 900.0));
        belt.DepositItem(belt.In, first);
        belt.Advance(TestContexts.Tick(0, dt: 0.5));   // first at 0.5
        belt.Advance(TestContexts.Tick(1, dt: 0.5));   // first at 1.0
        belt.DepositItem(belt.In, new ItemInstance(2, Billet, 5.0, default));

        Assert.True(belt.TryObserve(1.0, 0.1, out MaterialObservation seen));
        Assert.Equal(1L, seen.ItemId);
        Assert.Equal(900.0, seen.Properties.Temperature);
        Assert.Equal(5.0, seen.Mass);
        Assert.Equal(0.0, seen.LinearDensity);

        Assert.True(belt.TryObserve(0.05, 0.1, out MaterialObservation tail));
        Assert.Equal(2L, tail.ItemId);
        Assert.False(belt.TryObserve(2.0, 0.1, out _));
    }

    [Fact]
    public void BeltsDeclareNoDirectFeedthrough()
    {
        Assert.False(new BulkBelt("B", 1.0, 0.5, 1.0, 1.0).HasDirectFeedthrough);
        Assert.False(new DiscreteBelt("D", 1.0, 1.0).HasDirectFeedthrough);
    }
}
