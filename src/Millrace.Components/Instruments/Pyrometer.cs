using Millrace.Core.Catalogue;
using Millrace.Core.Contexts;
using Millrace.Core.Flow;
using Millrace.Core.Graph;

namespace Millrace.Components.Instruments;

/// <summary>
/// A non-contact temperature sensor aimed at one point on a node. Reads the
/// temperature of whatever material is within its window, and the background
/// when nothing is.
/// </summary>
public sealed class Pyrometer : InstrumentBase
{
    public static ComponentDescriptor Descriptor { get; } = new(
        "pyrometer",
        ComponentCategory.Instrumentation,
        "Reads the temperature of whatever is in its window; reads the background when nothing is.",
        (id, p) => new Pyrometer(
            id, p.Reference<IMaterialObservable>("target"), p.Double("positionM"), p.Double("windowM"), InstrumentCatalogue.ReadSpec(p.Group("spec"))))
    {
        Parameters =
        [
            Param.Reference<IMaterialObservable>("target", "What it looks at: a belt, a conveyor, a chute or a process unit."),
            Param.Double(
                "positionM",
                "Centre of the window, from the tail. Ignored when the target has no length (a chute or a process unit), which reports its whole contents.",
                "m",
                min: 0.0),
            Param.Double("windowM", "Length of the window. Ignored when the target has no length.", "m", min: 0.0),
            Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec),
        ],
        Ports = [PortSpec.In<double>("Background", "°C", "Read when the window is empty; defaults to 20."), .. InstrumentCatalogue.Outputs],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag],
        Telemetry = [InstrumentCatalogue.Truth],
    };

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
