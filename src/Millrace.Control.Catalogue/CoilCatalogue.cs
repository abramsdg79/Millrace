using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>The <see cref="Coil"/> in a plant file: <c>{ "type": "coil", … }</c>.</summary>
public static class CoilCatalogue
{
    public static BlockDescriptor Descriptor { get; } = new(
        "coil",
        "An output coil: drives a Bool tag true while a condition is at its normal value and false otherwise, writing on its first scan and on each change.",
        (id, p) =>
        [
            ControlCatalogue.Output(id, "Energised", TagKind.Bool, string.Empty, "The condition is at its normal value; the output is driven true"),
        ],
        (id, period, p) => new Coil(id, ControlCatalogue.ConditionOf(p.Group("condition")), p.Tag("output"), period))
    {
        Parameters =
        [
            Param.Group("condition", "The Bool tag watched, and the value that energises the coil.", ControlCatalogue.ConditionGroup),
            Param.Tag("output", "The Bool tag the coil drives; claim it so nothing else writes it.", TagKind.Bool, writes: true),
        ],
    };
}
