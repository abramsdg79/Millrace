using Millrace.Components.Conveyors;
using Millrace.Components.Mechanical;

namespace Millrace.Components.Tests.Catalogue;

public class ConveyorFactoryTests
{
    [Fact]
    public void TheFactoryBuildsWhatTheConstructorBuilds()
    {
        var byHand = new Conveyor("X", new ConveyorOptions(
            LengthM: 10.0, CellSizeM: 0.5, BeltWidthM: 0.8, AngleOfReposeDeg: 20.0, MaterialDensityKgM3: 2000.0,
            EmptyBeltMassKg: 250.0, FrictionCoefficient: 0.04, PulleyDiameterM: 0.5, GearRatio: 20.0,
            Motor: new MotorRating(750.0, 150.0, 2.0), TailDragN: 80.0, PullKeys: 3));

        Conveyor fromJson = MechanicalFactoryTests.Build<Conveyor>(Conveyor.Descriptor, """
            { "lengthM": 10, "cellSizeM": 0.5, "beltWidthM": 0.8, "angleOfReposeDeg": 20, "materialDensityKgM3": 2000,
              "emptyBeltMassKg": 250, "frictionCoefficient": 0.04, "pulleyDiameterM": 0.5, "gearRatio": 20,
              "motor": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 }, "tailDragN": 80, "pullKeys": 3 }
            """);

        Assert.Equal(byHand.LeafComponents.Select(l => l.Id), fromJson.LeafComponents.Select(l => l.Id));
        Assert.Equal(byHand.ExposedPorts.Select(e => e.Key), fromJson.ExposedPorts.Select(e => e.Key));
        Assert.Equal(byHand.Belt.MaxSpeed, fromJson.Belt.MaxSpeed);
        Assert.Equal(byHand.Belt.MaxLinearDensity, fromJson.Belt.MaxLinearDensity);
        Assert.Equal(byHand.Motor.Rating, fromJson.Motor.Rating);
    }

    [Fact]
    public void TheDefaultsAreTheOptionsRecordsDefaults()
    {
        Conveyor fromJson = MechanicalFactoryTests.Build<Conveyor>(Conveyor.Descriptor, """
            { "lengthM": 10, "cellSizeM": 0.5, "beltWidthM": 0.8, "angleOfReposeDeg": 20, "materialDensityKgM3": 2000,
              "emptyBeltMassKg": 250, "frictionCoefficient": 0.04, "pulleyDiameterM": 0.5, "gearRatio": 20,
              "motor": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 } }
            """);
        var byHand = new Conveyor("X", new ConveyorOptions(
            10.0, 0.5, 0.8, 20.0, 2000.0, 250.0, 0.04, 0.5, 20.0, new MotorRating(750.0, 150.0, 2.0)));

        Assert.Equal(byHand.LeafComponents.Count, fromJson.LeafComponents.Count);
        Assert.Equal(byHand.Belt.MaxSpeed, fromJson.Belt.MaxSpeed);
        Assert.Equal(byHand.Tail.BearingDragN, fromJson.Tail.BearingDragN);
    }
}
