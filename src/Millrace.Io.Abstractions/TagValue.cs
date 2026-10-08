using System.Globalization;

namespace Millrace.Io;

/// <summary>
/// One tag's value with its quality. Twenty-four bytes with padding, no references, immutable.
/// Equality compares kind, payload bits and quality, so a quality change alone
/// counts as a change. Doubles compare by bit pattern.
/// </summary>
public readonly record struct TagValue
{
    private readonly long _bits;

    private TagValue(TagKind kind, long bits, TagQuality quality)
    {
        Kind = kind;
        _bits = bits;
        Quality = quality;
    }

    /// <summary>The kind of the payload.</summary>
    public TagKind Kind { get; }

    /// <summary>The quality carried with the value.</summary>
    public TagQuality Quality { get; }

    /// <summary>A discrete value.</summary>
    public static TagValue Bool(bool value, TagQuality quality = default) =>
        new(TagKind.Bool, value ? 1L : 0L, quality);

    /// <summary>An analog value.</summary>
    public static TagValue Double(double value, TagQuality quality = default) =>
        new(TagKind.Double, BitConverter.DoubleToInt64Bits(value), quality);

    /// <summary>An integer value.</summary>
    public static TagValue Int64(long value, TagQuality quality = default) =>
        new(TagKind.Int64, value, quality);

    /// <summary>The payload as a bool. Throws if the kind is not <see cref="TagKind.Bool"/>.</summary>
    public bool AsBool => Expect(TagKind.Bool) != 0L;

    /// <summary>The payload as a double. Throws if the kind is not <see cref="TagKind.Double"/>.</summary>
    public double AsDouble => BitConverter.Int64BitsToDouble(Expect(TagKind.Double));

    /// <summary>The payload as a long. Throws if the kind is not <see cref="TagKind.Int64"/>.</summary>
    public long AsInt64 => Expect(TagKind.Int64);

    /// <summary>The same payload with a different quality.</summary>
    public TagValue WithQuality(TagQuality quality) => new(Kind, _bits, quality);

    /// <summary>True when kind and payload match, ignoring quality.</summary>
    public bool ValueEquals(TagValue other) => Kind == other.Kind && _bits == other._bits;

    /// <summary>
    /// The tag kind for a CLR type. Only <see cref="bool"/>, <see cref="double"/>
    /// and <see cref="long"/> are tag types; anything else throws.
    /// </summary>
    public static TagKind KindOf<T>()
        where T : unmanaged
    {
        if (typeof(T) == typeof(bool))
        {
            return TagKind.Bool;
        }

        if (typeof(T) == typeof(double))
        {
            return TagKind.Double;
        }

        if (typeof(T) == typeof(long))
        {
            return TagKind.Int64;
        }

        throw new NotSupportedException(
            $"'{typeof(T).Name}' is not a tag type. Tags carry bool, double or long.");
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        string payload = Kind switch
        {
            TagKind.Bool => _bits != 0L ? "true" : "false",
            TagKind.Double => BitConverter.Int64BitsToDouble(_bits).ToString("R", CultureInfo.InvariantCulture),
            _ => _bits.ToString(CultureInfo.InvariantCulture),
        };

        return Quality.IsGood ? payload : $"{payload} [{Quality}]";
    }

    private long Expect(TagKind kind)
    {
        if (Kind != kind)
        {
            throw new InvalidOperationException(
                $"This tag value is a {Kind}, not a {kind}.");
        }

        return _bits;
    }
}
