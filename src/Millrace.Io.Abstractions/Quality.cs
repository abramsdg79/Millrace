namespace Millrace.Io;

/// <summary>
/// The quality code carried by every tag value (spec 9.5). Ordered so that
/// <c>default</c> is <see cref="Good"/>.
/// </summary>
public enum Quality
{
    /// <summary>The value is trustworthy.</summary>
    Good,

    /// <summary>The value is present but failed a plausibility check.</summary>
    Uncertain,

    /// <summary>The value must not be used.</summary>
    Bad,
}

/// <summary>The sub-status that explains a non-good quality.</summary>
public enum QualityDetail
{
    /// <summary>No further information.</summary>
    None,

    /// <summary>The transmitter has failed (fail-high, fail-low, disconnected).</summary>
    SensorFailure,

    /// <summary>The reading left the instrument's declared range.</summary>
    OutOfRange,
}

/// <summary>A quality code with its detail. <c>default</c> is Good.</summary>
public readonly record struct TagQuality(Quality Code, QualityDetail Detail)
{
    /// <summary>Good with no detail — the value every healthy tag carries.</summary>
    public static readonly TagQuality Good = new(Quality.Good, QualityDetail.None);

    /// <summary>True when <see cref="Code"/> is <see cref="Quality.Good"/>.</summary>
    public bool IsGood => Code == Quality.Good;

    /// <summary>Uncertain with the given detail.</summary>
    public static TagQuality Uncertain(QualityDetail detail) => new(Quality.Uncertain, detail);

    /// <summary>Bad with the given detail.</summary>
    public static TagQuality Bad(QualityDetail detail) => new(Quality.Bad, detail);

    /// <inheritdoc/>
    public override string ToString() =>
        Detail == QualityDetail.None ? Code.ToString() : $"{Code}:{Detail}";
}
