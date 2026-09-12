using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>
/// A non-contact temperature sensor aimed at one point on a node. Reads the
/// temperature of whatever material is within its window, and the background
/// when nothing is.
/// </summary>
public sealed class Pyrometer : InstrumentBase
{
    private readonly IMaterialObservable _target;

    public Pyrometer(string id, IMaterialObservable target, double positionM, double windowM, InstrumentSpec spec)
        : base(id, spec)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(positionM);
        ArgumentOutOfRangeException.ThrowIfNegative(windowM);
        _target = target;
        PositionM = positionM;
        WindowM = windowM;
        Background = AddInput<double>("Background", defaultValue: 20.0);
    }

    /// <summary>What the sensor sees when no material is in view, °C. Unconnected reads 20 °C.</summary>
    public InputPort<double> Background { get; }

    /// <summary>Metres from the node's inlet end.</summary>
    public double PositionM { get; }

    /// <summary>Metres either side of <see cref="PositionM"/> the sensor's view spans.</summary>
    public double WindowM { get; }

    protected override double Measure(in TickContext ctx) =>
        _target.TryObserve(PositionM, WindowM, out MaterialObservation seen)
            ? seen.Properties.Temperature
            : Background.Value;
}
