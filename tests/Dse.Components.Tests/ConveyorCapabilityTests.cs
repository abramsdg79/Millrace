using Dse.Components.Conveyors;
using Dse.Components.Mechanical;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Components.Tests;

public class ConveyorCapabilityTests
{
    private static readonly ConveyorOptions Options = new(
        LengthM: 10.0,
        CellSizeM: 0.5,
        BeltWidthM: 0.8,
        AngleOfReposeDeg: 20.0,
        MaterialDensityKgM3: 2000.0,
        EmptyBeltMassKg: 250.0,
        FrictionCoefficient: 0.04,
        PulleyDiameterM: 0.5,
        GearRatio: 20.0,
        Motor: new MotorRating(750.0, 150.0, 2.0));

    [Fact]
    public void AConveyorOffersItsBeltAsAnObservable()
    {
        var conveyor = new Conveyor("CV001", Options);

        bool ok = ((ICapabilityProvider)conveyor).TryGetCapability(typeof(IMaterialObservable), out object? instance);

        Assert.True(ok);
        Assert.Same(conveyor.Belt, instance);
    }

    [Fact]
    public void AConveyorOffersNothingElse()
    {
        var conveyor = new Conveyor("CV001", Options);

        bool ok = ((ICapabilityProvider)conveyor).TryGetCapability(typeof(IDisposable), out object? instance);

        Assert.False(ok);
        Assert.Null(instance);
    }
}
