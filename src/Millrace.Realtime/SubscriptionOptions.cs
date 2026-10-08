namespace Millrace.Realtime;

/// <summary>What a subscriber declares when it subscribes (spec 10.2, 10.5, 10.8).</summary>
public sealed record SubscriptionOptions
{
    /// <summary>Backpressure policy; default Conflate.</summary>
    public BackpressurePolicy Policy { get; init; } = BackpressurePolicy.Conflate;

    /// <summary>
    /// Under Lossless, the number of undelivered deltas at which the
    /// subscription faults. Under Conflate, the number of pending events kept
    /// before further events are counted as dropped. Default 1024.
    /// </summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>
    /// Tag name prefixes to include; null means every tag. A tag matches when
    /// its name equals a prefix or starts with the prefix followed by a dot,
    /// so <c>CV001</c> selects the whole conveyor and <c>CV001.Speed</c> one tag.
    /// Events are never filtered.
    /// </summary>
    public IReadOnlyList<string>? Prefixes { get; init; }

    /// <summary>Absolute deadband per tag name, in the tag's unit. Double tags only.</summary>
    public IReadOnlyDictionary<string, double>? Deadbands { get; init; }

    /// <summary>
    /// A deadband as a percentage of the directory range, applied to every
    /// double tag that has a range and no entry in <see cref="Deadbands"/>.
    /// 0 (the default) means none.
    /// </summary>
    public double DeadbandPercentOfRange { get; init; }

    /// <summary>
    /// At most one delta per this much simulation time; frames in between are
    /// merged (latest value per tag, every event). Null means every frame.
    /// </summary>
    public TimeSpan? Decimation { get; init; }
}
