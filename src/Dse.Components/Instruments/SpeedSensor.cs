using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>A tachometer or encoder on a pulley: reports the speed on its input, m/s.</summary>
public sealed class SpeedSensor : InstrumentBase
{
    public SpeedSensor(string id, InstrumentSpec spec)
        : base(id, spec) => Speed = AddInput<double>("Speed");

    /// <summary>True speed, m/s.</summary>
    public InputPort<double> Speed { get; }

    protected override double Measure(in TickContext ctx) => Speed.Value;
}
