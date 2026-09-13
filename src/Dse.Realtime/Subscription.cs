using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// One consumer's view of the frame stream (spec 10.2, 10.5, 10.7). The hub
/// offers every frame on the pump thread; the subscription diffs it against the
/// values it last delivered and queues the result under its policy. The
/// consumer pulls with <see cref="TryRead"/> or waits on <see cref="Available"/>
/// (R27). A faulted subscription delivers nothing more and says why.
/// </summary>
public sealed class Subscription : IDisposable
{
    private readonly RealtimeHub _hub;
    private readonly object _sync = new();
    private readonly TagValue[] _lastSent;
    private readonly ManualResetEvent _available = new(false);
    private readonly Queue<FrameDelta>? _queue;
    private readonly DeltaAccumulator? _pending;
    private readonly List<TagChange> _scratch = [];
    private bool _faulted;
    private string? _faultReason;
    private bool _disposed;
    private long _delivered;

    internal Subscription(RealtimeHub hub, SubscriptionOptions options, StateSnapshot initial)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Capacity);

        _hub = hub;
        Options = options;
        Initial = initial;

        _lastSent = new TagValue[initial.Tags.Length];
        ReadOnlySpan<TagState> tags = initial.Tags.Span;
        for (int i = 0; i < _lastSent.Length; i++)
        {
            _lastSent[i] = tags[i].Value;
        }

        if (options.Policy == BackpressurePolicy.Lossless)
        {
            _queue = new Queue<FrameDelta>();
        }
        else
        {
            _pending = new DeltaAccumulator(_lastSent.Length, options.Capacity);
        }
    }

    /// <summary>What was declared at subscribe time.</summary>
    public SubscriptionOptions Options { get; }

    /// <summary>The state at subscribe time; deltas follow from the next tick (spec 10.7).</summary>
    public StateSnapshot Initial { get; }

    /// <summary>Set while a delta is readable or the subscription is faulted.</summary>
    public WaitHandle Available => _available;

    /// <summary>True once the subscription has been closed by the hub for cause.</summary>
    public bool IsFaulted
    {
        get
        {
            lock (_sync)
            {
                return _faulted;
            }
        }
    }

    /// <summary>Why it faulted, or null.</summary>
    public string? FaultReason
    {
        get
        {
            lock (_sync)
            {
                return _faultReason;
            }
        }
    }

    /// <summary>Deltas handed to the consumer so far.</summary>
    public long Delivered
    {
        get
        {
            lock (_sync)
            {
                return _delivered;
            }
        }
    }

    /// <summary>Under Conflate, events discarded because the pending delta was at capacity.</summary>
    public long DroppedEvents
    {
        get
        {
            lock (_sync)
            {
                return _pending?.DroppedEvents ?? 0L;
            }
        }
    }

    /// <summary>Deltas waiting: the queue length under Lossless, 0 or 1 under Conflate.</summary>
    public int Queued
    {
        get
        {
            lock (_sync)
            {
                return _queue?.Count ?? (_pending!.HasContent ? 1 : 0);
            }
        }
    }

    /// <summary>Takes the next delta if there is one. Never blocks.</summary>
    public bool TryRead(out FrameDelta delta)
    {
        lock (_sync)
        {
            if (_faulted || _disposed)
            {
                delta = null!;
                return false;
            }

            if (_queue is not null)
            {
                if (!_queue.TryDequeue(out FrameDelta? dequeued))
                {
                    delta = null!;
                    return false;
                }

                delta = dequeued;
            }
            else if (_pending!.HasContent)
            {
                delta = _pending.Flush();
            }
            else
            {
                delta = null!;
                return false;
            }

            _delivered++;
            if (_queue is { Count: 0 } || (_queue is null && !_pending!.HasContent))
            {
                _available.Reset();
            }

            return true;
        }
    }

    /// <summary>Pump thread: diffs the frame against the last delivered values and queues the result.</summary>
    internal void Offer(TickFrame frame)
    {
        lock (_sync)
        {
            if (_faulted || _disposed)
            {
                return;
            }

            Collect(frame, _scratch);
            if (_scratch.Count == 0 && frame.Events.Count == 0)
            {
                return;
            }

            Deliver(frame.Tick, frame.SimTime, _scratch, frame.Events);
            _scratch.Clear();
        }
    }

    /// <summary>Pump thread: the ring dropped frames since the last pump.</summary>
    internal void NotifyGap(long dropped)
    {
        lock (_sync)
        {
            if (_queue is not null)
            {
                FaultLocked($"ring overflow: {dropped} frame(s) dropped; a Lossless subscription cannot continue.");
            }
        }
    }

    /// <summary>Closes the subscription for cause.</summary>
    internal void Fault(string reason)
    {
        lock (_sync)
        {
            FaultLocked(reason);
        }
    }

    /// <summary>Unsubscribes. Idempotent.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue?.Clear();
        }

        _hub.Unsubscribe(this);
        _available.Dispose();
    }

    private void Collect(TickFrame frame, List<TagChange> into)
    {
        ReadOnlySpan<TagValue> values = frame.Values.Span;
        for (int i = 0; i < _lastSent.Length; i++)
        {
            if (values[i] == _lastSent[i])
            {
                continue;
            }

            into.Add(new TagChange(i, values[i]));
            _lastSent[i] = values[i];
        }
    }

    private void Deliver(long tick, DateTimeOffset simTime, List<TagChange> changes, IReadOnlyList<DiscreteEvent> events)
    {
        if (_queue is not null)
        {
            if (_queue.Count >= Options.Capacity)
            {
                FaultLocked($"lossless queue overflow: {Options.Capacity} undelivered delta(s); the consumer is too slow.");
                return;
            }

            _queue.Enqueue(new FrameDelta(tick, simTime, changes.ToArray(), events));
        }
        else
        {
            _pending!.Add(tick, simTime, changes, events);
        }

        _available.Set();
    }

    private void FaultLocked(string reason)
    {
        if (_faulted || _disposed)
        {
            return;
        }

        _faulted = true;
        _faultReason = reason;
        _queue?.Clear();
        _available.Set();
    }
}
