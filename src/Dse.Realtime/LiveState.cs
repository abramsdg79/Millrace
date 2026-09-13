using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// The live state engine (spec 10.3): current value, quality and last-change
/// tick per tag, plus the most recent events. Written by the hub's pump, read
/// by anyone; every member takes the private lock because <see cref="TagState"/>
/// is wider than a machine word.
/// </summary>
public sealed class LiveState
{
    private readonly object _sync = new();
    private readonly TagState[] _tags;
    private readonly DiscreteEvent[] _recent;
    private int _recentHead;
    private int _recentCount;
    private long _tick = -1;
    private DateTimeOffset _simTime;
    private long _framesApplied;

    /// <summary>Creates an empty state for <paramref name="directory"/>.</summary>
    public LiveState(ITagDirectory directory, int recentEventCapacity = 256)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recentEventCapacity);

        Directory = directory;
        _tags = new TagState[directory.Count];
        for (int i = 0; i < _tags.Length; i++)
        {
            TagValue initial = directory[i].Kind switch
            {
                TagKind.Bool => TagValue.Bool(false),
                TagKind.Double => TagValue.Double(0.0),
                _ => TagValue.Int64(0L),
            };
            _tags[i] = new TagState(initial, -1, default);
        }

        _recent = new DiscreteEvent[recentEventCapacity];
    }

    /// <summary>The directory this state indexes.</summary>
    public ITagDirectory Directory { get; }

    /// <summary>The last tick applied, or -1 before the first frame.</summary>
    public long Tick
    {
        get
        {
            lock (_sync)
            {
                return _tick;
            }
        }
    }

    /// <summary>Simulation time of the last applied frame.</summary>
    public DateTimeOffset SimTime
    {
        get
        {
            lock (_sync)
            {
                return _simTime;
            }
        }
    }

    /// <summary>Frames applied so far.</summary>
    public long FramesApplied
    {
        get
        {
            lock (_sync)
            {
                return _framesApplied;
            }
        }
    }

    /// <summary>The current state of the tag at <paramref name="index"/>.</summary>
    public TagState this[int index]
    {
        get
        {
            lock (_sync)
            {
                return _tags[index];
            }
        }
    }

    /// <summary>The current state of a named tag.</summary>
    public TagState Get(string name) => this[Directory.Find(name).Index];

    /// <summary>An immutable copy of everything, taken atomically.</summary>
    public StateSnapshot Snapshot()
    {
        lock (_sync)
        {
            var tags = new TagState[_tags.Length];
            Array.Copy(_tags, tags, tags.Length);

            var recent = new DiscreteEvent[_recentCount];
            int start = (_recentHead - _recentCount + _recent.Length) % _recent.Length;
            for (int i = 0; i < _recentCount; i++)
            {
                recent[i] = _recent[(start + i) % _recent.Length];
            }

            return new StateSnapshot(_tick, _simTime, tags, recent);
        }
    }

    /// <summary>
    /// Applies a frame. The first frame is applied in full; later frames apply
    /// their dirty tags only. Called by the hub's pump, under the hub's lock.
    /// </summary>
    internal void Apply(TickFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Values.Length != _tags.Length)
        {
            throw new ArgumentException(
                $"The frame carries {frame.Values.Length} tags; this state has {_tags.Length}.", nameof(frame));
        }

        lock (_sync)
        {
            bool first = _framesApplied == 0;
            ReadOnlySpan<TagValue> values = frame.Values.Span;
            for (int i = 0; i < _tags.Length; i++)
            {
                if (first || frame.Dirty[i])
                {
                    _tags[i] = new TagState(values[i], frame.Tick, frame.SimTime);
                }
            }

            for (int i = 0; i < frame.Events.Count; i++)
            {
                _recent[_recentHead] = frame.Events[i];
                _recentHead = (_recentHead + 1) % _recent.Length;
                if (_recentCount < _recent.Length)
                {
                    _recentCount++;
                }
            }

            _tick = frame.Tick;
            _simTime = frame.SimTime;
            _framesApplied++;
        }
    }
}
