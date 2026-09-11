using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Dse.Core.Tests.Fakes.Flow;
using Xunit;

namespace Dse.Core.Tests;

public class DiscreteBeltTests
{
    private const double Dt = 0.5;

    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete, "TimeAbove100");

    private static DiscreteBelt NewBelt(double minSpacing = 0.0, params IMaterialTransform[] transforms)
    {
        var belt = new DiscreteBelt("CV", length: 2.0, maxSpeed: 1.0, minSpacing, transforms);
        belt.Initialize(TestContexts.Init(belt.Id, dt: Dt));
        return belt;
    }

    private static OutputPort<double> Drive(DiscreteBelt belt, double speed)
    {
        var setpoint = new OutputPort<double>("Out", "SP");
        setpoint.ConnectTo(belt.Speed);
        setpoint.Value = speed;
        return setpoint;
    }

    private static ItemInstance Item(long id, MaterialType? type = null, double mass = 1.0) =>
        new(id, type ?? Wheel, mass, new MaterialProperties(7800.0, 0.0, 20.0));

    private static double[] Positions(DiscreteBelt belt) => belt.Items.Select(c => c.Position).ToArray();

    [Fact]
    public void RejectsInvalidConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscreteBelt("CV", 0.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscreteBelt("CV", 2.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscreteBelt("CV", 2.0, 1.0, minSpacing: -1.0));
        Assert.Throws<ArgumentException>(() => new DiscreteBelt("CV", 2.0, 1.0, minSpacing: 3.0));
    }

    [Fact]
    public void ItemsAdvanceBySpeedTimesDt()
    {
        DiscreteBelt belt = NewBelt();
        Drive(belt, 1.0);
        belt.DepositItem(belt.In, Item(1));

        belt.Advance(Dt);
        Assert.Equal(new[] { 0.5 }, Positions(belt));

        belt.Advance(Dt);
        Assert.Equal(new[] { 1.0 }, Positions(belt));
    }

    [Fact]
    public void TheHeadItemIsOfferedOnlyAtTheEnd()
    {
        DiscreteBelt belt = NewBelt();
        Drive(belt, 1.0);
        ItemInstance item = Item(1);
        belt.DepositItem(belt.In, item);

        for (int i = 0; i < 3; i++)
        {
            belt.Advance(Dt);
            Assert.False(belt.TryPeekItem(belt.Out, out _));
        }

        belt.Advance(Dt);

        Assert.True(belt.TryPeekItem(belt.Out, out ItemInstance? head));
        Assert.Same(item, head);
        Assert.Equal(2.0, belt.Items[0].Position, 9);
    }

    [Fact]
    public void WithdrawRemovesTheHeadAndThrowsWhenNothingHasArrived()
    {
        DiscreteBelt belt = NewBelt();
        Drive(belt, 1.0);
        ItemInstance item = Item(1);
        belt.DepositItem(belt.In, item);
        for (int i = 0; i < 4; i++)
        {
            belt.Advance(Dt);
        }

        Assert.Same(item, belt.WithdrawItem(belt.Out));
        Assert.Empty(belt.Items);
        Assert.Throws<InvalidOperationException>(() => belt.WithdrawItem(belt.Out));
    }

    [Fact]
    public void ItemsQueueBehindABlockedHead()
    {
        DiscreteBelt belt = NewBelt(minSpacing: 0.5);
        Drive(belt, 1.0);
        belt.DepositItem(belt.In, Item(1));
        belt.Advance(Dt);
        belt.DepositItem(belt.In, Item(2));

        for (int i = 0; i < 6; i++)
        {
            belt.Advance(Dt);
        }

        Assert.Equal(new[] { 2.0, 1.5 }, Positions(belt));
        Assert.True(belt.TryPeekItem(belt.Out, out ItemInstance? head));
        Assert.Equal(1, head!.Id);
    }

    [Fact]
    public void MinSpacingBlocksTheTailUntilTheLastItemHasMovedClear()
    {
        DiscreteBelt belt = NewBelt(minSpacing: 0.5);
        Drive(belt, 1.0);
        belt.DepositItem(belt.In, Item(1));

        Assert.False(belt.CanAcceptItem(belt.In, Item(2)));
        belt.Advance(Dt);
        Assert.True(belt.CanAcceptItem(belt.In, Item(2)));
    }

    [Fact]
    public void ZeroSpacingAlwaysAccepts()
    {
        DiscreteBelt belt = NewBelt();
        belt.DepositItem(belt.In, Item(1));

        Assert.True(belt.CanAcceptItem(belt.In, Item(2)));
        belt.DepositItem(belt.In, Item(2));
        Assert.Equal(new[] { 0.0, 0.0 }, Positions(belt));
    }

    [Fact]
    public void AStoppedBeltHoldsItsItems()
    {
        DiscreteBelt belt = NewBelt();
        OutputPort<double> speed = Drive(belt, 1.0);
        belt.DepositItem(belt.In, Item(1));
        belt.Advance(Dt);

        speed.Value = 0.0;
        belt.Advance(Dt);
        belt.Advance(Dt);

        Assert.Equal(new[] { 0.5 }, Positions(belt));
    }

    [Fact]
    public void TransformsAccumulateItemStateUsingTheMaterialSchema()
    {
        int slot = Billet.StateIndexOf("TimeAbove100");
        DiscreteBelt belt = NewBelt(0.0, new Heater(ratePerSecond: 1.0), new ResidenceCounter(100.0, slot));
        var ambient = new OutputPort<double>("Out", "Zone");
        ambient.ConnectTo(belt.AmbientTemperature);
        ambient.Value = 200.0;
        ItemInstance billet = Item(1, Billet, 12.0);
        belt.DepositItem(belt.In, billet);

        belt.Advance(Dt);
        Assert.Equal(110.0, billet.Properties.Temperature, 9);
        Assert.Equal(0.5, billet.State[slot], 9);

        belt.Advance(Dt);
        Assert.Equal(155.0, billet.Properties.Temperature, 9);
        Assert.Equal(1.0, billet.State[slot], 9);
    }

    [Fact]
    public void ItemCountOutputAndMassHeldReflectTheItems()
    {
        DiscreteBelt belt = NewBelt();
        belt.DepositItem(belt.In, Item(1, mass: 1.0));
        belt.DepositItem(belt.In, Item(2, mass: 2.5));

        belt.Evaluate(TestContexts.Tick(0, Dt));

        Assert.Equal(2, belt.ItemCount.Value);
        Assert.Equal(3.5, belt.MassHeld, 9);
    }

    [Fact]
    public void SpeedOutsideTheDeclaredRangeThrows()
    {
        DiscreteBelt belt = NewBelt();
        OutputPort<double> speed = Drive(belt, 1.5);

        Assert.Throws<InvalidOperationException>(() => belt.Advance(Dt));

        speed.Value = -0.5;
        Assert.Throws<InvalidOperationException>(() => belt.Advance(Dt));
    }
}
