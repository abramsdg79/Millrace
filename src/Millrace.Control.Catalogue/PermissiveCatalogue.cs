using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>The <see cref="Permissive"/> in a plant file: <c>{ "type": "permissive", … }</c>.</summary>
public static class PermissiveCatalogue
{
    public static BlockDescriptor Descriptor { get; } = new(
        "permissive",
        "The conditions something needs before it may start: Ok while every condition is normal, never latched.",
        (id, p) =>
        [
            ControlCatalogue.Output(id, "Ok", TagKind.Bool, string.Empty, "Every condition is normal"),
            ControlCatalogue.Output(id, "FirstOut", TagKind.Int64, string.Empty, "Index of the first condition to leave normal, or -1"),
        ],
        (id, period, p) => new Permissive(id, ControlCatalogue.Conditions(p, "conditions"), period))
    {
        Parameters =
        [
            Param.GroupList("conditions", "The Bool tags that must be normal, in report order.", ControlCatalogue.ConditionGroup, minCount: 1),
        ],
    };
}
