using Dse.Io;

namespace Dse.Realtime;

/// <summary>An immutable copy of the live state at one tick: what a late joiner receives first.</summary>
public sealed class StateSnapshot
{
    /// <summary>Creates a snapshot; the caller promises never to write the arrays again.</summary>
    public StateSnapshot(long tick, DateTimeOffset simTime, ReadOnlyMemory<TagState> tags, IReadOnlyList<DiscreteEvent> recentEvents)
    {
        ArgumentNullException.ThrowIfNull(recentEvents);
        Tick = tick;
        SimTime = simTime;
        Tags = tags;
        RecentEvents = recentEvents;
    }

    /// <summary>The last tick applied, or -1 if none.</summary>
    public long Tick { get; }

    /// <summary>Simulation time of that tick.</summary>
    public DateTimeOffset SimTime { get; }

    /// <summary>Every tag, by directory index.</summary>
    public ReadOnlyMemory<TagState> Tags { get; }

    /// <summary>The newest events, oldest first.</summary>
    public IReadOnlyList<DiscreteEvent> RecentEvents { get; }
}
