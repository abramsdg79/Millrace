using Millrace.Core.Catalogue;

namespace Millrace.Components.Transforms;

/// <summary>The transforms a belt or process unit can apply to what it carries.</summary>
public static class TransformDescriptors
{
    public static ObjectDescriptor Thermal { get; } = new(
        ObjectSlots.Transform,
        "thermal-transfer",
        "Moves the material's temperature towards ambient with a first-order time constant.",
        p => new ThermalTransfer(p.Double("timeConstantSeconds")))
    {
        Parameters = [Param.Double("timeConstantSeconds", "Time to close 63% of the gap to ambient. Zero snaps to ambient.", "s", min: 0.0)],
    };

    public static ObjectDescriptor Moisture { get; } = new(
        ObjectSlots.Transform,
        "moisture-loss",
        "Dries the material while it is hotter than a threshold, in proportion to the excess.",
        p => new MoistureLoss(p.Double("ratePerDegreeSecond"), p.Double("thresholdTemperature")))
    {
        Parameters =
        [
            Param.Double("ratePerDegreeSecond", "Moisture fraction lost per degree above the threshold, per second.", "1/(°C·s)", min: 0.0),
            Param.Double("thresholdTemperature", "Temperature above which drying happens.", "°C"),
        ],
    };

    public static ObjectDescriptor Residence { get; } = new(
        ObjectSlots.Transform,
        "residence-accumulator",
        "Accumulates, in one of the material's states, the seconds spent at or above a threshold temperature.",
        p => new ResidenceAccumulator(p.StateIndex("state"), p.Double("thresholdTemperature")))
    {
        Parameters =
        [
            Param.Material("material", "The material whose state is accumulated."),
            Param.MaterialState("state", "The state that accumulates.", "material"),
            Param.Double("thresholdTemperature", "Temperature at or above which time counts.", "°C"),
        ],
    };
}
