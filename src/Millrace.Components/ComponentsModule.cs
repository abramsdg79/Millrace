using Millrace.Components.Conveyors;
using Millrace.Components.Flow;
using Millrace.Components.Instruments;
using Millrace.Components.Mechanical;
using Millrace.Components.Safety;
using Millrace.Components.Transforms;
using Millrace.Core.Catalogue;

namespace Millrace.Components;

/// <summary>
/// Everything a plant can instantiate out of the box: this assembly's
/// components, transforms and hold conditions, and the nodes Core ships.
/// </summary>
public sealed class ComponentsModule : ICatalogueModule
{
    public string Name => "Millrace.Components";

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

        // Flow
        builder.Add(CoreDescriptors.BeltBulk);
        builder.Add(CoreDescriptors.BeltDiscrete);
        builder.Add(BulkSource.Descriptor);
        builder.Add(BulkSink.Descriptor);
        builder.Add(ItemSource.Descriptor);
        builder.Add(ItemSink.Descriptor);
        builder.Add(TransferChute.Descriptor);
        builder.Add(Former.Descriptor);
        builder.Add(BulkProcessUnit.Descriptor);
        builder.Add(ItemProcessUnit.Descriptor);
        builder.Add(RejectGate.Descriptor);

        // Conveyor
        builder.Add(Conveyor.Descriptor);

        // Transforms
        builder.Add(TransformDescriptors.Thermal);
        builder.Add(TransformDescriptors.Moisture);
        builder.Add(TransformDescriptors.Residence);

        // Hold conditions
        foreach (ObjectDescriptor hold in HoldDescriptors.All)
        {
            builder.Add(hold);
        }
    }
}
