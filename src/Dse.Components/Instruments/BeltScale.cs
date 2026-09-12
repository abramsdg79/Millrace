using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>
/// A belt weigher: linear density under the weigh idler times belt speed,
/// reported in t/h. Mounted on a bulk belt at a position from its tail; reads
/// the belt's material in phase 2, when nothing moves. The datasheet lag is
/// the scale's integration window.
/// </summary>
public sealed class BeltScale : InstrumentBase
{
    private const double KgPerSecondToTonnesPerHour = 3.6;

    private readonly IMaterialObservable _belt;

    public BeltScale(string id, IMaterialObservable belt, double positionM, InstrumentSpec spec)
        : base(id, spec)
    {
        ArgumentNullException.ThrowIfNull(belt);
        ArgumentOutOfRangeException.ThrowIfNegative(positionM);
        _belt = belt;
        PositionM = positionM;
        Speed = AddInput<double>("Speed");
    }

    /// <summary>Belt speed at the weigh idler, m/s.</summary>
    public InputPort<double> Speed { get; }

    /// <summary>Metres from the belt's tail.</summary>
    public double PositionM { get; }

    protected override double Measure(in TickContext ctx) =>
        _belt.TryObserve(PositionM, 0.0, out MaterialObservation seen)
            ? seen.LinearDensity * Speed.Value * KgPerSecondToTonnesPerHour
            : 0.0;
}
