using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>A thermocouple or RTD on a signal: reports the temperature on its input, °C.</summary>
public sealed class TemperatureSensor : InstrumentBase
{
    public TemperatureSensor(string id, InstrumentSpec spec)
        : base(id, spec) => Temperature = AddInput<double>("Temperature");

    /// <summary>True temperature, °C.</summary>
    public InputPort<double> Temperature { get; }

    protected override double Measure(in TickContext ctx) => Temperature.Value;
}
