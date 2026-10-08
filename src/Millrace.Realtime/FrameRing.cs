using Millrace.Io;

namespace Millrace.Realtime;

/// <summary>
/// A single-producer, single-consumer ring of frames. The producer owns
/// <c>_head</c>, the consumer owns <c>_tail</c>; each publishes its index with
/// a volatile write after touching the slot, and reads the other's with a
/// volatile read. Full means the producer's enqueue fails (R26); it never
/// touches the consumer's index.
/// </summary>
internal sealed class FrameRing
{
    private readonly TickFrame?[] _slots;
    private long _head;
    private long _tail;

    public FrameRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _slots = new TickFrame?[capacity];
    }

    public int Capacity => _slots.Length;

    public int Count => (int)(Volatile.Read(ref _head) - Volatile.Read(ref _tail));

    /// <summary>Producer side. False when full; the frame is then the caller's to drop.</summary>
    public bool TryEnqueue(TickFrame frame)
    {
        long head = _head;
        if (head - Volatile.Read(ref _tail) >= _slots.Length)
        {
            return false;
        }

        _slots[head % _slots.Length] = frame;
        Volatile.Write(ref _head, head + 1);
        return true;
    }

    /// <summary>Consumer side.</summary>
    public bool TryDequeue(out TickFrame frame)
    {
        long tail = _tail;
        if (tail >= Volatile.Read(ref _head))
        {
            frame = null!;
            return false;
        }

        int slot = (int)(tail % _slots.Length);
        frame = _slots[slot]!;
        _slots[slot] = null;
        Volatile.Write(ref _tail, tail + 1);
        return true;
    }
}
