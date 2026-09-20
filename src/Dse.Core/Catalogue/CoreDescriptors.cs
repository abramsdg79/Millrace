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
}
