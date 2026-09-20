using System.Text.Json;
using Dse.Components.Mechanical;
using Dse.Core.Catalogue;

namespace Dse.Components.Tests.Catalogue;

public class MechanicalFactoryTests
{
    internal static T Build<T>(ComponentDescriptor descriptor, string json, BindingContext? context = null)
        where T : class
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(
            descriptor.Parameters, document.RootElement, "$", context ?? new BindingContext(ComponentsFixtures.Catalogue), construct: true, issues);
        Assert.Empty(issues);
        return Assert.IsType<T>(descriptor.Factory("X", values!));
    }

    [Fact]
    public void TheMotorFactoryReadsTheWholeRating()
    {
        Motor motor = Build<Motor>(Motor.Descriptor, """
            { "rating": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2,
                          "noLoadCurrentFraction": 0.31, "lockedRotorCurrentMultiple": 6.1, "breakdownTorqueMultiple": 2.6,
                          "accelerationTimeConstantS": 1.1, "coastTimeConstantS": 3.1, "thermalTimeConstantS": 61,
                          "speedDroopFraction": 0.04 } }
            """);

        Assert.Equal(new MotorRating(750, 150, 2, 0.31, 6.1, 2.6, 1.1, 3.1, 61, 0.04), motor.Rating);
        Assert.Equal("X", motor.Id);
    }

    [Fact]
    public void TheMotorFactoryAppliesTheRatingDefaults()
    {
        Motor motor = Build<Motor>(Motor.Descriptor, """{ "rating": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 } }""");

        Assert.Equal(new MotorRating(750, 150, 2), motor.Rating);
    }

    [Fact]
    public void TheGearboxAndStarterFactoriesPassTheirArguments()
    {
        Gearbox gearbox = Build<Gearbox>(Gearbox.Descriptor, """{ "ratio": 20, "efficiency": 0.9 }""");
        MotorStarter starter = Build<MotorStarter>(MotorStarter.Descriptor, """{ "tripLevel": 1.2, "resetLevel": 0.8 }""");

        Assert.Equal(20.0, gearbox.Ratio);
        Assert.Equal(0.9, gearbox.Efficiency);
        Assert.Equal(1.2, starter.TripLevel);
        Assert.Equal(0.8, starter.ResetLevel);
    }
}
