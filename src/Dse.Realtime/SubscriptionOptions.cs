namespace Dse.Realtime;

/// <summary>What a subscriber declares when it subscribes.</summary>
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
}
