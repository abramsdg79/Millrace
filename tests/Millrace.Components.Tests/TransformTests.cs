using Millrace.Components.Tests.Fakes;
using Millrace.Components.Transforms;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Xunit;

namespace Millrace.Components.Tests;

public class TransformTests
{
    private static readonly MaterialType Dough = new("Dough", PayloadKind.Bulk);
    private static readonly MaterialType Loaf = new("Loaf", PayloadKind.Discrete, "BakeTimeAbove200C", "CoreTemperature");

    [Fact]
    public void ThermalTransferMovesTowardAmbientWithTheTimeConstant()
    {
        var transform = new ThermalTransfer(timeConstantSeconds: 10.0);
        var props = new MaterialProperties(1000.0, 0.4, 20.0);
        var ctx = new TransformContext(220.0);

        for (int i = 0; i < 100; i++)
        {
            transform.Apply(ref props, Span<double>.Empty, 0.1, in ctx);   // 10 s = one time constant
        }

        Assert.InRange(props.Temperature, 145.0, 148.0);   // exact 146.4; forward Euler lands within a degree
        Assert.Equal(0.4, props.Moisture);
        Assert.Equal(1000.0, props.Density);
    }

    [Fact]
    public void ThermalTransferWithAZeroTimeConstantSnapsToAmbient()
    {
        var transform = new ThermalTransfer(0.0);
        var props = new MaterialProperties(1000.0, 0.4, 20.0);
        transform.Apply(ref props, Span<double>.Empty, 0.01, new TransformContext(180.0));
        Assert.Equal(180.0, props.Temperature);
    }

    [Fact]
    public void MoistureLossOnlyHappensAboveTheThresholdAndNeverGoesNegative()
    {
        var transform = new MoistureLoss(ratePerDegreeSecond: 0.001, thresholdTemperature: 100.0);
        var cold = new MaterialProperties(1000.0, 0.4, 60.0);
        var hot = new MaterialProperties(1000.0, 0.4, 150.0);
        var ctx = new TransformContext(200.0);

        transform.Apply(ref cold, Span<double>.Empty, 1.0, in ctx);
        transform.Apply(ref hot, Span<double>.Empty, 1.0, in ctx);
        Assert.Equal(0.4, cold.Moisture);
        Assert.Equal(0.35, hot.Moisture, 9);   // 0.001 × 50 °C × 1 s

        for (int i = 0; i < 20; i++)
        {
            transform.Apply(ref hot, Span<double>.Empty, 1.0, in ctx);
        }

        Assert.Equal(0.0, hot.Moisture);
    }

    [Fact]
    public void ResidenceAccumulatorCountsTimeAboveTheThresholdIntoItsSlot()
    {
        ResidenceAccumulator transform = ResidenceAccumulator.For(Loaf, "BakeTimeAbove200C", 200.0);
        double[] state = Loaf.NewState();
        var below = new MaterialProperties(600.0, 0.3, 150.0);
        var above = new MaterialProperties(600.0, 0.3, 210.0);
        var ctx = new TransformContext(230.0);

        transform.Apply(ref below, state, 0.5, in ctx);
        transform.Apply(ref above, state, 0.5, in ctx);
        transform.Apply(ref above, state, 0.5, in ctx);

        Assert.Equal([1.0, 0.0], state);
        Assert.Equal(150.0, below.Temperature);

        transform.Apply(ref above, Span<double>.Empty, 0.5, in ctx);   // bulk: no slot, no-op, no throw
        Assert.Throws<KeyNotFoundException>(() => ResidenceAccumulator.For(Loaf, "Crust", 200.0));
    }

    [Fact]
    public void ABeltWithTransformsBakesWhatItCarries()
    {
        var oven = new DiscreteBelt(
            "Oven",
            length: 2.0,
            maxSpeed: 1.0,
            transforms: [new ThermalTransfer(1.0), ResidenceAccumulator.For(Loaf, "BakeTimeAbove200C", 200.0)]);
        oven.Initialize(TestContexts.Init(oven.Id, dt: 0.1));
        var speed = new OutputPort<double>("Out", "SP");
        speed.ConnectTo(oven.Speed);
        speed.Value = 0.5;
        var zone = new OutputPort<double>("Out", "Zone");
        zone.ConnectTo(oven.AmbientTemperature);
        zone.Value = 230.0;

        var loaf = new ItemInstance(1, Loaf, 0.8, new MaterialProperties(600.0, 0.3, 25.0));
        oven.DepositItem(oven.In, loaf);
        for (int tick = 0; tick < 40; tick++)
        {
            oven.Advance(TestContexts.Tick(tick, dt: 0.1));   // 4 s at 0.5 m/s: reaches the head
        }

        Assert.True(loaf.Properties.Temperature > 200.0);
        Assert.True(loaf.State[0] > 0.5 && loaf.State[0] < 4.0);
        Assert.True(oven.TryPeekItem(oven.Out, out _));
    }

    [Fact]
    public void ABulkBeltWithMoistureLossDriesWhatItCarries()
    {
        var dryer = new BulkBelt(
            "Dryer",
            length: 1.0,
            cellSize: 0.5,
            maxSpeed: 1.0,
            maxLinearDensity: 10.0,
            transforms: [new ThermalTransfer(0.0), new MoistureLoss(0.001, 100.0)]);
        dryer.Initialize(TestContexts.Init(dryer.Id, dt: 0.1));
        var zone = new OutputPort<double>("Out", "Zone");
        zone.ConnectTo(dryer.AmbientTemperature);
        zone.Value = 150.0;
        dryer.Deposit(dryer.In, BulkLot.Of(Dough, 1.0, new MaterialProperties(1000.0, 0.4, 20.0)));

        for (int tick = 0; tick < 10; tick++)
        {
            dryer.Advance(TestContexts.Tick(tick, dt: 0.1));   // speed 0: nothing moves, everything dries
        }

        Assert.Equal(1.0, dryer.MassHeld, 9);
        Assert.Equal(0.4 - (0.001 * 50.0 * 1.0), dryer.Cells[0].Properties.Moisture, 9);   // 0.35
    }
}
