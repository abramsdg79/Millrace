using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Components.Instruments;

/// <summary>What every <see cref="InstrumentBase"/> contributes to its descriptor.</summary>
public static class InstrumentCatalogue
{
    /// <summary><see cref="InstrumentSpec"/> as catalogue parameters.</summary>
    public static GroupDefinition Spec { get; } = new(
        "InstrumentSpec",
        Param.String("unit", "Engineering unit of the reading; becomes the tag's unit."),
        Param.Double("rangeLow", "Bottom of the calibrated range. The reading clamps here."),
        Param.Double("rangeHigh", "Top of the calibrated range; must exceed rangeLow."),
        Param.Double("noiseSigma", "Standard deviation of Gaussian noise per tick, in the reading's unit.", @default: 0.0, min: 0.0),
        Param.Double("lagSeconds", "First-order response time constant.", "s", @default: 0.0, min: 0.0));

    /// <summary>The two outputs every instrument has.</summary>
    public static IReadOnlyList<PortDescriptor> Outputs { get; } =
    [
        PortSpec.Out<double>("Value", description: "The reading, in the spec's unit, clamped to its range."),
        PortSpec.Out<TagQuality>("Health", description: "Good, Uncertain when saturated, Bad on a fail fault."),
    ];

    /// <summary>The reading's tag. Its unit and range come from the spec, so none is stated here.</summary>
    public static TagEntry ValueTag { get; } = new("Value", TagKind.Double, TagAccess.ReadOnly);

    /// <summary>The true value before the instrument touched it. Unit from the spec.</summary>
    public static TelemetryKey Truth { get; } = new("Truth");

    public static InstrumentSpec ReadSpec(ParameterValues p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new InstrumentSpec(
            p.String("unit"), p.Double("rangeLow"), p.Double("rangeHigh"), p.Double("noiseSigma"), p.Double("lagSeconds"));
    }
}
