namespace Millrace.Core.Events;

/// <summary>
/// A binary min-heap ordered by (due tick, sequence). The sequence number is
/// what makes simultaneous events reproducible: two events due on the same tick
/// always fire in the order they were scheduled.
/// </summary>
public sealed class EventQueue
{
    private readonly List<ScheduledEvent> _heap = [];
    private long _nextSequence;

    public int Count => _heap.Count;

    /// <summary>Schedules an event and returns its sequence number.</summary>
    public long Schedule(long dueTick, ISimEvent payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dueTick);
        ArgumentNullException.ThrowIfNull(payload);

        var scheduled = new ScheduledEvent(dueTick, _nextSequence++, payload);
        _heap.Add(scheduled);
        SiftUp(_heap.Count - 1);
        return scheduled.Sequence;
    }

    /// <summary>
    /// Removes and returns the earliest event due at or before <paramref name="tick"/>.
    /// Events that became due while nothing was draining are still delivered.
    /// </summary>
    public bool TryDequeueDue(long tick, out ScheduledEvent scheduled)
    {
        if (_heap.Count == 0 || _heap[0].DueTick > tick)
        {
            scheduled = default;
            return false;
        }

        scheduled = _heap[0];
        int last = _heap.Count - 1;
        _heap[0] = _heap[last];
        _heap.RemoveAt(last);
        if (_heap.Count > 0)
        {
            SiftDown(0);
        }

        return true;
    }

    private static bool IsBefore(in ScheduledEvent a, in ScheduledEvent b) =>
        a.DueTick != b.DueTick ? a.DueTick < b.DueTick : a.Sequence < b.Sequence;

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (!IsBefore(_heap[index], _heap[parent]))
            {
                return;
            }

            (_heap[index], _heap[parent]) = (_heap[parent], _heap[index]);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        while (true)
        {
            int left = (2 * index) + 1;
            int right = left + 1;
            int smallest = index;

            if (left < _heap.Count && IsBefore(_heap[left], _heap[smallest]))
            {
                smallest = left;
            }

            if (right < _heap.Count && IsBefore(_heap[right], _heap[smallest]))
            {
                smallest = right;
            }

            if (smallest == index)
            {
                return;
            }

            (_heap[index], _heap[smallest]) = (_heap[smallest], _heap[index]);
            index = smallest;
        }
    }
}
