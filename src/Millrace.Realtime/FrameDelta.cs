using Millrace.Io;

namespace Millrace.Realtime;

/// <summary>
/// What a subscriber reads: the tags that changed since its previous delta
/// (or since its initial snapshot) and the events in between. Under Conflate
/// or decimation a delta may span many frames; <see cref="Tick"/> is the
/// newest.
/// </summary>
public sealed record FrameDelta(
    long Tick,
    DateTimeOffset SimTime,
    IReadOnlyList<TagChange> Changes,
    IReadOnlyList<DiscreteEvent> Events);
