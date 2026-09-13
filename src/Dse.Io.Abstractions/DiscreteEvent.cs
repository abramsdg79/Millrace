namespace Dse.Io;

/// <summary>
/// One event-log record as it crosses the determinism boundary. The source is
/// a component id or, for external writes, a tag name.
/// </summary>
public sealed record DiscreteEvent(
    long Tick,
    DateTimeOffset SimTime,
    string Source,
    string Code,
    string Message);
