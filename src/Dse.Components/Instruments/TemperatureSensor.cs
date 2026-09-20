using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>A thermocouple or RTD on a signal: reports the temperature on its input, °C.</summary>
public sealed class TemperatureSensor : InstrumentBase
{
    public static ComponentDescriptor Descriptor { get; } = new(
        "temperature-sensor",
        ComponentCategory.Instrumentation,
        "Measures a temperature signal.",
        (id, p) => new TemperatureSensor(id, InstrumentCatalogue.ReadSpec(p.Group("spec"))))
    {
        Parameters = [Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec)],
        Ports = [PortSpec.In<double>("Temperature", "°C", "The true temperature."), .. InstrumentCatalogue.Outputs],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag],
        Telemetry = [InstrumentCatalogue.Truth],
    };

    public TemperatureSensor(string id, InstrumentSpec spec)
        : base(id, spec) => Temperature = AddInput<double>("Temperature");

    /// <summary>True temperature, °C.</summary>
    public InputPort<double> Temperature { get; }

    protected override double Measure(in TickContext ctx) => Temperature.Value;
}
