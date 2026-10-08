using Millrace.Io;

namespace Millrace.Realtime;

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
    private readonly bool[] _included;
    private readonly double[] _deadband;
    private readonly DeltaAccumulator? _window;
    private readonly TimeSpan _decimation;
    private DateTimeOffset? _lastEmit;
    private bool _faulted;
    private string? _faultReason;
    private bool _disposed;
    private long _delivered;
    private readonly long _droppedAtSubscribe;

    internal Subscription(RealtimeHub hub, SubscriptionOptions options, StateSnapshot initial, long droppedAtSubscribe)
    {
        _droppedAtSubscribe = droppedAtSubscribe;
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

        ITagDirectory directory = hub.Directory;
        if (directory.Count != _lastSent.Length)
        {
            throw new ArgumentException("The snapshot does not match the hub's directory.", nameof(initial));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.DeadbandPercentOfRange);
        if (options.Decimation is { } decimation)
        {
            if (decimation <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), decimation, "Decimation must be positive.");
            }

            _decimation = decimation;
            _window = new DeltaAccumulator(_lastSent.Length, int.MaxValue);
        }

        _included = new bool[_lastSent.Length];
        _deadband = new double[_lastSent.Length];
        for (int i = 0; i < _included.Length; i++)
        {
            TagDescriptor tag = directory[i];
            _included[i] = options.Prefixes is null || Matches(tag.Name, options.Prefixes);
            _deadband[i] = double.NaN;
            if (tag.Kind == TagKind.Double && options.DeadbandPercentOfRange > 0.0 && tag.HasRange)
            {
                _deadband[i] = (tag.RangeHigh - tag.RangeLow) * options.DeadbandPercentOfRange / 100.0;
            }
        }

        if (options.Deadbands is not null)
        {
            foreach ((string name, double band) in options.Deadbands)
            {
                if (!directory.TryFind(name, out TagDescriptor tag))
                {
                    throw new ArgumentException($"Deadband for unknown tag '{name}'.", nameof(options));
                }

                if (tag.Kind != TagKind.Double)
                {
                    throw new ArgumentException($"Deadband on '{name}', a {tag.Kind} tag; deadbands apply to double tags only.", nameof(options));
                }

                ArgumentOutOfRangeException.ThrowIfNegative(band, nameof(options));
                _deadband[tag.Index] = band;
            }
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
            bool content = _scratch.Count > 0 || frame.Events.Count > 0;

            if (_window is null)
            {
                if (content)
                {
                    Deliver(frame.Tick, frame.SimTime, _scratch, frame.Events);
                }
            }
            else
            {
                if (content)
                {
                    _window.Add(frame.Tick, frame.SimTime, _scratch, frame.Events);
                }

                if (_lastEmit is null || frame.SimTime - _lastEmit.Value >= _decimation)
                {
                    if (_window.HasContent)
                    {
                        FrameDelta merged = _window.Flush();
                        Deliver(merged.Tick, merged.SimTime, merged.Changes, merged.Events);
                    }

                    _lastEmit = frame.SimTime;
                }
            }

            _scratch.Clear();
        }
    }

    /// <summary>
    /// Pump thread: the hub's total dropped-frame count as of this pump. Faults a
    /// Lossless subscription only when frames were dropped since it subscribed
    /// (spec 10.7): a gap that predates the subscription is not its concern.
    /// </summary>
    internal void NotifyGap(long droppedTotal)
    {
        lock (_sync)
        {
            if (_queue is not null && droppedTotal > _droppedAtSubscribe)
            {
                long dropped = droppedTotal - _droppedAtSubscribe;
                FaultLocked($"ring overflow: {dropped} frame(s) dropped since subscribing; a Lossless subscription cannot continue.");
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
            if (!_included[i])
            {
                continue;
            }

            TagValue value = values[i];
            TagValue last = _lastSent[i];
            if (value == last)
            {
                continue;
            }

            double band = _deadband[i];
            if (!double.IsNaN(band)
                && value.Quality == last.Quality
                && Math.Abs(value.AsDouble - last.AsDouble) < band)
            {
                continue;
            }

            into.Add(new TagChange(i, value));
            _lastSent[i] = value;
        }
    }

    private void Deliver(long tick, DateTimeOffset simTime, IReadOnlyList<TagChange> changes, IReadOnlyList<DiscreteEvent> events)
    {
        if (_queue is not null)
        {
            if (_queue.Count >= Options.Capacity)
            {
                FaultLocked($"lossless queue overflow: {Options.Capacity} undelivered delta(s); the consumer is too slow.");
                return;
            }

            _queue.Enqueue(new FrameDelta(tick, simTime, changes as TagChange[] ?? changes.ToArray(), events));
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

    private static bool Matches(string name, IReadOnlyList<string> prefixes)
    {
        for (int i = 0; i < prefixes.Count; i++)
        {
            string prefix = prefixes[i];
            if (name.Length == prefix.Length)
            {
                if (string.Equals(name, prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (name.Length > prefix.Length
                     && name[prefix.Length] == '.'
                     && name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
