using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;
using Dse.Core.Tests.Fakes;
using Dse.Core.Tests.Fakes.Flow;
using Xunit;

namespace Dse.Core.Tests;

public class BulkBeltTransformTests
{
    private const double Dt = 0.5;

    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialProperties Cold = new(1600.0, 0.1, 0.0);

    private static TickContext NewTick(long tick = 0) => TestContexts.Tick(tick, Dt);

    private static BulkBelt NewBelt(params IMaterialTransform[] transforms)
    {
        var belt = new BulkBelt("CV", 2.5, 0.5, 1.0, 4.0, transforms);
        belt.Initialize(TestContexts.Init(belt.Id, dt: Dt));
        return belt;
    }

    private static OutputPort<double> Drive(BulkBelt belt, double speed)
    {
        var setpoint = new OutputPort<double>("Out", "SP");
        setpoint.ConnectTo(belt.Speed);
        setpoint.Value = speed;
        return setpoint;
    }

    [Fact]
    public void TransformsApplyToResidentMaterialEveryAdvanceEvenWhenStopped()
    {
        BulkBelt belt = NewBelt(new Heater(ratePerSecond: 1.0));
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.0, Cold));

        belt.Advance(NewTick());
        Assert.Equal(10.0, belt.Cells[0].Properties.Temperature, 9);

        belt.Advance(NewTick());
        Assert.Equal(15.0, belt.Cells[0].Properties.Temperature, 9);
    }

    [Fact]
    public void AmbientComesFromTheSignalInput()
    {
        BulkBelt belt = NewBelt(new Heater(1.0));
        var ambient = new OutputPort<double>("Out", "Zone");
        ambient.ConnectTo(belt.AmbientTemperature);
        ambient.Value = 100.0;
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.0, Cold));

        belt.Advance(NewTick());

        Assert.Equal(50.0, belt.Cells[0].Properties.Temperature, 9);
    }

    [Fact]
    public void TransformsRunBeforeTheCellsMove()
    {
        BulkBelt belt = NewBelt(new Heater(1.0));
        Drive(belt, 1.0);
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.0, Cold));

        belt.Advance(NewTick());

        Assert.Equal(0.0, belt.Cells[0].Mass);
        Assert.Equal(10.0, belt.Cells[1].Properties.Temperature, 9);
    }

    [Fact]
    public void EmptyCellsAreNotTransformed()
    {
        var counter = new CountingTransform();
        BulkBelt belt = NewBelt(counter);
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.0, Cold));

        belt.Advance(NewTick());

        Assert.Equal(1, counter.Calls);
        Assert.Equal(0, counter.StateLengthSeen);
    }

    [Fact]
    public void BeltsWithoutTransformsLeavePropertiesAlone()
    {
        BulkBelt belt = NewBelt();
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.0, Cold));

        belt.Advance(NewTick());

        Assert.Equal(Cold, belt.Cells[0].Properties);
    }

    [Fact]
    public void LoadAndPeakDensityOutputsReflectTheCells()
    {
        BulkBelt belt = NewBelt();
        Drive(belt, 1.0);
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.5, Cold));
        belt.Advance(NewTick());
        belt.Deposit(belt.In, BulkLot.Of(Ore, 0.5, Cold));

        belt.Evaluate(TestContexts.Tick(0, Dt));

        Assert.Equal(2.0, belt.Load.Value, 9);
        Assert.Equal(3.0, belt.PeakLinearDensity.Value, 9);
    }

    [Fact]
    public void LoadIsPublishedAsTelemetry()
    {
        var telemetry = new TelemetryRegistry();
        var belt = new BulkBelt("CV001", 2.5, 0.5, 1.0, 4.0);
        belt.Initialize(TestContexts.Init(belt.Id, dt: Dt, telemetry: telemetry));
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.25, Cold));

        belt.Evaluate(TestContexts.Tick(0, Dt));

        Assert.Equal(1.25, telemetry.Read("CV001.Load"), 9);
        Assert.Equal("kg", Assert.Single(telemetry.Channels).Unit);
    }

    private sealed class CountingTransform : IMaterialTransform
    {
        public int Calls { get; private set; }

        public int StateLengthSeen { get; private set; }

        public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context)
        {
            Calls++;
            StateLengthSeen = state.Length;
        }
    }
}
