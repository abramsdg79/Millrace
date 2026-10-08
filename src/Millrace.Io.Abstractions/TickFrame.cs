namespace Millrace.Io;

/// <summary>
/// The only thing that crosses out of the engine (spec 10.1): one tick's
/// complete tag image, which of those tags changed, and the tick's discrete
/// events. Immutable; the arrays behind it are never written after publish,
/// so any number of consumers may hold a frame for any length of time.
/// </summary>
public sealed class TickFrame
{
    /// <summary>Creates a frame. <paramref name="dirty"/> must cover exactly <paramref name="values"/>.</summary>
    public TickFrame(
        long tick,
        DateTimeOffset simTime,
        ReadOnlyMemory<TagValue> values,
        DirtyMask dirty,
        IReadOnlyList<DiscreteEvent> events)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        ArgumentNullException.ThrowIfNull(events);
        if (dirty.Length != values.Length)
        {
            throw new ArgumentException(
                $"The dirty mask covers {dirty.Length} tags but the frame carries {values.Length}.",
                nameof(dirty));
        }

        Tick = tick;
        SimTime = simTime;
        Values = values;
        Dirty = dirty;
        Events = events;
    }

    /// <summary>The tick index this frame describes.</summary>
    public long Tick { get; }

    /// <summary>The tick's simulation timestamp — the value components saw as <c>TickContext.SimTime</c>. Never wall-clock (spec 10.8).</summary>
    public DateTimeOffset SimTime { get; }

    /// <summary>Every tag's value, by directory index.</summary>
    public ReadOnlyMemory<TagValue> Values { get; }

    /// <summary>Which tags differ from the previous frame.</summary>
    public DirtyMask Dirty { get; }

    /// <summary>The event-log records this tick produced, in order.</summary>
    public IReadOnlyList<DiscreteEvent> Events { get; }
}
