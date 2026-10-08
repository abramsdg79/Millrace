using Millrace.Core.Catalogue;
using Millrace.Core.Contexts;
using Millrace.Core.Flow;
using Millrace.Core.Graph;

namespace Millrace.Components.Instruments;

/// <summary>
/// A belt weigher: linear density under the weigh idler times belt speed,
/// reported in t/h. Mounted on a bulk belt at a position from its tail; reads
/// the belt's material in phase 2, when nothing moves. The datasheet lag is
/// the scale's integration window.
/// </summary>
public sealed class BeltScale : InstrumentBase
{
    private const double KgPerSecondToTonnesPerHour = 3.6;

    public static ComponentDescriptor Descriptor { get; } = new(
        "belt-scale",
        ComponentCategory.Instrumentation,
        "Weighs the material passing one point of a belt and reports a mass flow from the load there and the belt speed.",
        (id, p) => new BeltScale(id, p.Reference<IMaterialObservable>("belt"), p.Double("positionM"), InstrumentCatalogue.ReadSpec(p.Group("spec"))))
    {
        Parameters =
        [
            Param.Reference<IMaterialObservable>("belt", "The belt weighed: a belt, or a conveyor that has one."),
            Param.Double("positionM", "Distance of the weigh frame from the tail.", "m", min: 0.0),
            Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec),
        ],
        Ports = [PortSpec.In<double>("Speed", "m/s", "Belt speed."), .. InstrumentCatalogue.Outputs],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag],
        Telemetry = [InstrumentCatalogue.Truth],
    };

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
