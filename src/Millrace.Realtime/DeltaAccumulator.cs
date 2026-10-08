using Millrace.Io;

namespace Millrace.Realtime;

/// <summary>
/// Merges successive changes into one pending delta: latest value per tag,
/// events appended up to a capacity. Used for the Conflate policy's pending
/// slot and (Task 11) for decimation windows. Not thread-safe; the owning
/// subscription locks.
/// </summary>
internal sealed class DeltaAccumulator
{
    private readonly TagValue[] _values;
    private readonly bool[] _set;
    private readonly List<DiscreteEvent> _events = [];
    private readonly int _eventCapacity;
    private int _count;
    private long _tick;
    private DateTimeOffset _simTime;

    public DeltaAccumulator(int tagCount, int eventCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tagCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventCapacity);
        _values = new TagValue[tagCount];
        _set = new bool[tagCount];
        _eventCapacity = eventCapacity;
    }

    public bool HasContent => _count > 0 || _events.Count > 0;

    public long DroppedEvents { get; private set; }

    public void Add(long tick, DateTimeOffset simTime, IReadOnlyList<TagChange> changes, IReadOnlyList<DiscreteEvent> events)
    {
        for (int i = 0; i < changes.Count; i++)
        {
            TagChange change = changes[i];
            if (!_set[change.Index])
            {
                _set[change.Index] = true;
                _count++;
            }

            _values[change.Index] = change.Value;
        }

        for (int i = 0; i < events.Count; i++)
        {
            if (_events.Count >= _eventCapacity)
            {
                DroppedEvents++;
                continue;
            }

            _events.Add(events[i]);
        }

        _tick = tick;
        _simTime = simTime;
    }

    public FrameDelta Flush()
    {
        var changes = new TagChange[_count];
        int k = 0;
        for (int i = 0; i < _values.Length && k < changes.Length; i++)
        {
            if (_set[i])
            {
                changes[k++] = new TagChange(i, _values[i]);
                _set[i] = false;
            }
        }

        _count = 0;
        DiscreteEvent[] events = _events.ToArray();
        _events.Clear();
        return new FrameDelta(_tick, _simTime, changes, events);
    }
}
