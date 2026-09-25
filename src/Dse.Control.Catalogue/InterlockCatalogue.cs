using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Control.Catalogue;

/// <summary>The <see cref="Interlock"/> in a plant file: <c>{ "type": "interlock", … }</c>.</summary>
public static class InterlockCatalogue
{
    public static BlockDescriptor Descriptor { get; } = new(
        "interlock",
        "The conditions that stop a running thing: any abnormal condition latches Tripped and sends the trip writes once, until Reset.",
        (id, p) =>
        [
            ControlCatalogue.Output(id, "Ok", TagKind.Bool, string.Empty, "Not tripped"),
            ControlCatalogue.Output(id, "Tripped", TagKind.Bool, string.Empty, "Latched by an abnormal condition"),
            ControlCatalogue.Output(id, "FirstOut", TagKind.Int64, string.Empty, "Index of the condition that tripped, or -1"),
            ControlCatalogue.Command(id, "Reset", "Clears the latch on a rising edge when every condition is normal"),
        ],
        (id, period, p) => new Interlock(
            id, ControlCatalogue.Conditions(p, "conditions"), ControlCatalogue.Writes(p, "trip"), period))
    {
        Parameters =
        [
            Param.GroupList("conditions", "The Bool tags whose abnormal value trips the interlock, in report order.", ControlCatalogue.ConditionGroup, minCount: 1),
            Param.GroupList("trip", "What to command when the interlock trips, once, on the trip scan.", ControlCatalogue.WriteGroup),
        ],
    };
}
