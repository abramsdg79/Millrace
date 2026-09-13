namespace Dse.Io;

/// <summary>
/// One entry of the tag directory (spec 9.1): everything a consumer, an HMI
/// generator or a protocol adapter needs to know about a tag without touching
/// the model. <see cref="RangeLow"/> and <see cref="RangeHigh"/> are NaN when
/// the tag declares no range.
/// </summary>
/// <param name="Index">Position in the image and in every frame's value array.</param>
/// <param name="Name">Full ordinal name, e.g. <c>CV001.Scale.Value</c>.</param>
/// <param name="Kind">The value kind.</param>
/// <param name="Access">Whether external writes are accepted.</param>
/// <param name="Unit">Engineering unit; empty for discrete tags.</param>
/// <param name="RangeLow">Lower engineering-range bound, or NaN.</param>
/// <param name="RangeHigh">Upper engineering-range bound, or NaN.</param>
/// <param name="Description">A sentence fragment for humans and for OPC UA's Description.</param>
public sealed record TagDescriptor(
    int Index,
    string Name,
    TagKind Kind,
    TagAccess Access,
    string Unit,
    double RangeLow,
    double RangeHigh,
    string Description)
{
    /// <summary>True when both range bounds are numbers.</summary>
    public bool HasRange => !double.IsNaN(RangeLow) && !double.IsNaN(RangeHigh);
}
