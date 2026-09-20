using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>A current transformer on a motor feed: reports the current on its input, A.</summary>
public sealed class CurrentSensor : InstrumentBase
{
    public static ComponentDescriptor Descriptor { get; } = new(
        "current-sensor",
        ComponentCategory.Instrumentation,
        "Measures a current signal.",
        (id, p) => new CurrentSensor(id, InstrumentCatalogue.ReadSpec(p.Group("spec"))))
    {
        Parameters = [Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec)],
        Ports = [PortSpec.In<double>("Current", "A", "The true current."), .. InstrumentCatalogue.Outputs],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag],
        Telemetry = [InstrumentCatalogue.Truth],
    };

    public CurrentSensor(string id, InstrumentSpec spec)
        : base(id, spec) => Current = AddInput<double>("Current");

    /// <summary>True current, A.</summary>
    public InputPort<double> Current { get; }

    protected override double Measure(in TickContext ctx) => Current.Value;
}
