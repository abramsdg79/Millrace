using Dse.Core.Catalogue;

namespace Dse.Components.Flow;

/// <summary>The conditions a process unit can hold a batch for. See <see cref="Hold"/>.</summary>
public static class HoldDescriptors
{
    public static IReadOnlyList<ObjectDescriptor> All { get; } =
    [
        new(ObjectSlots.Hold, "for-seconds", "Holds the batch for a fixed time.", p => Hold.ForSeconds(p.Double("seconds")))
        {
            Parameters = [Param.Double("seconds", "How long to hold.", "s", min: 0.0)],
        },
        new(ObjectSlots.Hold, "temperature-at-least", "Holds until the batch is at least this hot.", p => Hold.TemperatureAtLeast(p.Double("celsius")))
        {
            Parameters = [Param.Double("celsius", "The temperature to reach.", "°C")],
        },
        new(ObjectSlots.Hold, "temperature-at-most", "Holds until the batch has cooled to this.", p => Hold.TemperatureAtMost(p.Double("celsius")))
        {
            Parameters = [Param.Double("celsius", "The temperature to fall to.", "°C")],
        },
        new(ObjectSlots.Hold, "state-at-least", "Holds until one of the material's states reaches a value.", p => Hold.StateAtLeast(p.StateIndex("state"), p.Double("value")))
        {
            Parameters =
            [
                Param.Material("material", "The material whose state is watched."),
                Param.MaterialState("state", "The state watched.", "material"),
                Param.Double("value", "The value to reach."),
            ],
        },
        new(ObjectSlots.Hold, "all", "Holds until every listed condition is satisfied.", p => Hold.All(p.Objects<IHoldCondition>("conditions").ToArray()))
        {
            Parameters = [Param.ObjectList("conditions", "The conditions, all of which must hold.", ObjectSlots.Hold, minCount: 1)],
        },
    ];
}
