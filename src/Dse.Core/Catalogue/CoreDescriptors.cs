using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Core.Catalogue;

/// <summary>
/// Descriptors for the nodes Core itself ships. A generic node is registered
/// once per closed type a plant can name.
/// </summary>
public static class CoreDescriptors
{
    public static ComponentDescriptor UnitDelayBool { get; } = new(
        "unit-delay-bool",
        ComponentCategory.Signal,
        "Holds a boolean for one tick. Put one on a connection to break an algebraic loop.",
        (id, p) => new UnitDelay<bool>(id, p.Bool("initialValue")))
    {
        Parameters = [Param.Bool("initialValue", "The output on the first tick.", @default: false)],
        Ports =
        [
            PortSpec.In<bool>("In", description: "The value to delay."),
            PortSpec.Out<bool>("Out", description: "The input as it was one tick ago."),
        ],
    };

    public static ComponentDescriptor UnitDelayDouble { get; } = new(
        "unit-delay-double",
        ComponentCategory.Signal,
        "Holds a number for one tick. Put one on a connection to break an algebraic loop.",
        (id, p) => new UnitDelay<double>(id, p.Double("initialValue")))
    {
        Parameters = [Param.Double("initialValue", "The output on the first tick.", @default: 0.0)],
        Ports =
        [
            PortSpec.In<double>("In", description: "The value to delay."),
            PortSpec.Out<double>("Out", description: "The input as it was one tick ago."),
        ],
    };

    private static readonly ParameterDescriptor Transforms = Param.ObjectList(
        "transforms", "Applied, in order, every tick to everything the belt carries.", ObjectSlots.Transform);

    public static ComponentDescriptor BeltBulk { get; } = new(
        "bulk-belt",
        ComponentCategory.Flow,
        "A spatially discretised belt for bulk material: what is loaded travels, and stops where it is when the belt stops.",
        (id, p) => new BulkBelt(
            id, p.Double("lengthM"), p.Double("cellSizeM"), p.Double("maxSpeedMps"), p.Double("maxLinearDensityKgPerM"),
            p.Objects<IMaterialTransform>("transforms")))
    {
        Parameters =
        [
            Param.Double("lengthM", "Belt length; must be a whole number of cells.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("cellSizeM", "Length of one cell. The belt may not advance more than one cell per tick (DSE006).", "m", min: 0.0, exclusiveMin: true),
            Param.Double("maxSpeedMps", "The fastest the belt will ever be driven.", "m/s", min: 0.0, exclusiveMin: true),
            Param.Double("maxLinearDensityKgPerM", "The most the belt can carry per metre.", "kg/m", min: 0.0, exclusiveMin: true),
            Transforms,
        ],
        Ports =
        [
            PortSpec.In<double>("Speed", "m/s"),
            PortSpec.In<double>("AmbientTemperature", "°C", "Defaults to 20."),
            PortSpec.Out<double>("Load", "kg", "Total mass on the belt."),
            PortSpec.Out<double>("PeakLinearDensity", "kg/m"),
        ],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk, "The tail."), PortSpec.Outlet("Out", PayloadKind.Bulk, "The head.")],
        Telemetry = [new TelemetryKey("Load", "kg")],
        Provides = [typeof(IMaterialObservable)],
    };

    public static ComponentDescriptor BeltDiscrete { get; } = new(
        "discrete-belt",
        ComponentCategory.Flow,
        "A belt carrying individual items at tracked positions, with an optional minimum spacing.",
        (id, p) => new DiscreteBelt(
            id, p.Double("lengthM"), p.Double("maxSpeedMps"), p.Double("minSpacingM"), p.Objects<IMaterialTransform>("transforms")))
    {
        Parameters =
        [
            Param.Double("lengthM", "Belt length.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("maxSpeedMps", "The fastest the belt will ever be driven.", "m/s", min: 0.0, exclusiveMin: true),
            Param.Double("minSpacingM", "Closest two items may sit; must not exceed the length.", "m", @default: 0.0, min: 0.0),
            Transforms,
        ],
        Ports =
        [
            PortSpec.In<double>("Speed", "m/s"),
            PortSpec.In<double>("AmbientTemperature", "°C", "Defaults to 20."),
            PortSpec.Out<int>("ItemCount", "count"),
        ],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Discrete, "The tail."), PortSpec.Outlet("Out", PayloadKind.Discrete, "The head.")],
        Provides = [typeof(IMaterialObservable)],
    };
}
