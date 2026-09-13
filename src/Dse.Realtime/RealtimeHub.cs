using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// The real-time event engine (spec 10.1, 10.2): owns the ring the simulation
/// publishes into, the pump that fans frames out to the live state and to every
/// subscription, and the no-gap subscribe. <see cref="Publish"/> is the
/// simulation thread's only obligation and never blocks. <see cref="Pump"/> is
/// called by a dispatcher thread in production and directly in tests.
/// </summary>
public sealed class RealtimeHub : ITickFrameSink, IDisposable
{
    private readonly FrameRing _ring;
    private readonly object _pumpLock = new();
    private readonly List<Subscription> _subscriptions = [];
    private readonly AutoResetEvent _framesAvailable = new(false);
    private long _published;
    private long _dropped;
    private long _droppedSinceLastPump;
    private bool _disposed;

    /// <summary>Creates a hub for <paramref name="directory"/> with a ring of <paramref name="ringCapacity"/> frames.</summary>
    public RealtimeHub(ITagDirectory directory, int ringCapacity = 4096, int recentEventCapacity = 256)
    {
        ArgumentNullException.ThrowIfNull(directory);
        Directory = directory;
        _ring = new FrameRing(ringCapacity);
        State = new LiveState(directory, recentEventCapacity);
    }

    /// <summary>The directory frames are indexed by.</summary>
    public ITagDirectory Directory { get; }

    /// <summary>Current truth, maintained by the pump (spec 10.3).</summary>
    public LiveState State { get; }

    /// <summary>Frames accepted into the ring.</summary>
    public long PublishedFrames => Volatile.Read(ref _published);

    /// <summary>Frames dropped because the ring was full (R26).</summary>
    public long DroppedFrames => Volatile.Read(ref _dropped);

    /// <summary>Frames in the ring not yet pumped.</summary>
    public int Pending => _ring.Count;

    /// <summary>Signalled on every publish; what a dispatcher waits on.</summary>
    public WaitHandle FramesAvailable => _framesAvailable;

    /// <summary>Live subscriptions.</summary>
    public int SubscriberCount
    {
        get
        {
            lock (_pumpLock)
            {
                return _subscriptions.Count;
            }
        }
    }

    /// <inheritdoc/>
    public void Publish(TickFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_ring.TryEnqueue(frame))
        {
            Interlocked.Increment(ref _published);
        }
        else
        {
            Interlocked.Increment(ref _dropped);
            Interlocked.Increment(ref _droppedSinceLastPump);
        }

        if (!_disposed)
        {
            _framesAvailable.Set();
        }
    }

    /// <summary>
    /// Drains the ring: applies each frame to <see cref="State"/>, then offers
    /// it to every subscription. Returns the number of frames processed. Safe
    /// to call from any single thread at a time; concurrent callers serialise.
    /// </summary>
    public int Pump()
    {
        lock (_pumpLock)
        {
            long gap = Interlocked.Exchange(ref _droppedSinceLastPump, 0L);
            if (gap > 0L)
            {
                foreach (Subscription subscription in _subscriptions)
                {
                    subscription.NotifyGap(gap);
                }
            }

            int processed = 0;
            while (_ring.TryDequeue(out TickFrame frame))
            {
                State.Apply(frame);
                foreach (Subscription subscription in _subscriptions)
                {
                    subscription.Offer(frame);
                }

                processed++;
            }

            return processed;
        }
    }

    /// <summary>
    /// Captures the state snapshot and registers the subscription in one step
    /// under the pump lock, so no frame can fall between them (spec 10.7).
    /// </summary>
    public Subscription Subscribe(SubscriptionOptions? options = null)
    {
        lock (_pumpLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var subscription = new Subscription(this, options ?? new SubscriptionOptions(), State.Snapshot());
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    internal void Unsubscribe(Subscription subscription)
    {
        lock (_pumpLock)
        {
            _subscriptions.Remove(subscription);
        }
    }

    /// <summary>Releases the wait handle. Dispose the dispatcher first.</summary>
    public void Dispose()
    {
        lock (_pumpLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _framesAvailable.Dispose();
    }
}
