using Millrace.Components.Instruments;
using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Components.Tests.Fakes;

/// <summary>Measures whatever is on its input, so the base pipeline can be tested alone.</summary>
public sealed class ProbeInstrument : InstrumentBase
{
    public ProbeInstrument(string id, InstrumentSpec spec)
        : base(id, spec) => In = AddInput<double>("In");

    public InputPort<double> In { get; }

    protected override double Measure(in TickContext ctx) => In.Value;
}
