using Millrace.Core.Catalogue;
using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Components.Instruments;

/// <summary>A tachometer or encoder on a pulley: reports the speed on its input, m/s.</summary>
public sealed class SpeedSensor : InstrumentBase
{
    public static ComponentDescriptor Descriptor { get; } = new(
        "speed-sensor",
        ComponentCategory.Instrumentation,
        "Measures a speed signal.",
        (id, p) => new SpeedSensor(id, InstrumentCatalogue.ReadSpec(p.Group("spec"))))
    {
        Parameters = [Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec)],
        Ports = [PortSpec.In<double>("Speed", description: "The true speed."), .. InstrumentCatalogue.Outputs],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag],
        Telemetry = [InstrumentCatalogue.Truth],
    };

    public SpeedSensor(string id, InstrumentSpec spec)
        : base(id, spec) => Speed = AddInput<double>("Speed");

    /// <summary>True speed, m/s.</summary>
    public InputPort<double> Speed { get; }

    protected override double Measure(in TickContext ctx) => Speed.Value;
}
