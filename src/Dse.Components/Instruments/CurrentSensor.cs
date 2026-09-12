using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>A current transformer on a motor feed: reports the current on its input, A.</summary>
public sealed class CurrentSensor : InstrumentBase
{
    public CurrentSensor(string id, InstrumentSpec spec)
        : base(id, spec) => Current = AddInput<double>("Current");

    /// <summary>True current, A.</summary>
    public InputPort<double> Current { get; }

    protected override double Measure(in TickContext ctx) => Current.Value;
}
