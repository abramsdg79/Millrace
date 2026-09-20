using Dse.Components.Instruments;
using Dse.Components.Mechanical;
using Dse.Components.Safety;
using Dse.Core.Catalogue;

namespace Dse.Components;

/// <summary>
/// Everything a plant can instantiate out of the box: this assembly's
/// components, transforms and hold conditions, and the nodes Core ships.
/// </summary>
public sealed class ComponentsModule : ICatalogueModule
{
    public string Name => "Dse.Components";

    public void Register(CatalogueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Signal
        builder.Add(CoreDescriptors.UnitDelayBool);
        builder.Add(CoreDescriptors.UnitDelayDouble);

        // Mechanical
        builder.Add(Motor.Descriptor);
        builder.Add(Gearbox.Descriptor);
        builder.Add(DrivePulley.Descriptor);
        builder.Add(TailPulley.Descriptor);
        builder.Add(BeltFriction.Descriptor);
        builder.Add(MotorStarter.Descriptor);

        // Instrumentation
        builder.Add(SpeedSensor.Descriptor);
        builder.Add(CurrentSensor.Descriptor);
        builder.Add(TemperatureSensor.Descriptor);
        builder.Add(BeltScale.Descriptor);
        builder.Add(Pyrometer.Descriptor);
        builder.Add(ZeroSpeedSwitch.Descriptor);
        builder.Add(PartCounter.Descriptor);

        // Safety
        builder.Add(EStop.Descriptor);
        builder.Add(PullKey.Descriptor);
        builder.Add(SafetyRelay.Descriptor);
    }
}
