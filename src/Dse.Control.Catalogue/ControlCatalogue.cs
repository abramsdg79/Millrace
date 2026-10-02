using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Control.Catalogue;

/// <summary>What the block descriptors share: the condition and write groups, the transition slot, the duration bound.</summary>
public static class ControlCatalogue
{
    /// <summary>The object slot a sequencer step's transition comes from.</summary>
    public const string TransitionSlot = "transition";

    /// <summary>
    /// The longest duration a block parameter accepts: a year, in seconds.
    /// <c>TimeSpan.FromSeconds</c> overflows near 9.2e11 s, so without a bound an
    /// absurd duration would surface as an overflow rather than a range error (R86).
    /// </summary>
    public const double MaxSeconds = 31_536_000.0;

    /// <summary>A Bool tag with the value that means "normal" — a permissive's or an interlock's condition.</summary>
    public static GroupDefinition ConditionGroup { get; } = new(
        "Condition",
        Param.Tag("tag", "The Bool tag watched.", TagKind.Bool),
        Param.Bool("normal", "The value that means the condition is satisfied."));

    /// <summary>A tag a block commands and the value it commands — a trip, entry or abort write.</summary>
    public static GroupDefinition WriteGroup { get; } = new(
        "BlockWrite",
        Param.Tag("tag", "The tag commanded.", writes: true),
        Param.Value("value", "The value commanded.", "tag"));

    /// <summary>A duration in seconds: zero or more, at most <see cref="MaxSeconds"/>.</summary>
    internal static ParameterDescriptor Seconds(string name, string description, double? @default = null) =>
        Param.Double(name, description, "s", @default, min: 0.0, max: MaxSeconds);

    internal static IReadOnlyList<Condition> Conditions(ParameterValues p, string name) =>
        p.Groups(name).Select(ConditionOf).ToList();

    internal static Condition ConditionOf(ParameterValues group) => new(group.Tag("tag"), group.Bool("normal"));

    internal static IReadOnlyList<BlockWrite> Writes(ParameterValues p, string name) =>
        p.Groups(name).Select(g => new BlockWrite(g.Tag("tag"), g.Value("value"))).ToList();

    internal static (TagSpec Spec, TagAccess Access) Output(string id, string pin, TagKind kind, string unit, string description) =>
        (new TagSpec($"{id}.{pin}", kind, unit, description), TagAccess.ReadOnly);

    internal static (TagSpec Spec, TagAccess Access) Command(string id, string pin, string description) =>
        (new TagSpec($"{id}.{pin}", TagKind.Bool, string.Empty, description), TagAccess.ReadWrite);
}
