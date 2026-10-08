namespace Millrace.Io;

/// <summary>
/// Which tag indices changed in a frame, as an immutable bitset. A change is
/// any difference in <see cref="TagValue"/> equality — payload or quality.
/// </summary>
public readonly struct DirtyMask
{
    private readonly ReadOnlyMemory<ulong> _bits;

    private DirtyMask(ReadOnlyMemory<ulong> bits, int length, int count)
    {
        _bits = bits;
        Length = length;
        Count = count;
    }

    /// <summary>Number of tags the mask covers.</summary>
    public int Length { get; }

    /// <summary>Number of set bits.</summary>
    public int Count { get; }

    /// <summary>Whether the tag at <paramref name="index"/> changed.</summary>
    public bool this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Length);
            return (_bits.Span[index >> 6] & (1UL << (index & 63))) != 0UL;
        }
    }

    /// <summary>A mask of the given length with no bits set.</summary>
    public static DirtyMask Empty(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return new DirtyMask(new ulong[WordsFor(length)], length, 0);
    }

    /// <summary>A mask of the given length with every bit set — the first frame's mask.</summary>
    public static DirtyMask All(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var words = new ulong[WordsFor(length)];
        for (int i = 0; i < length; i++)
        {
            words[i >> 6] |= 1UL << (i & 63);
        }

        return new DirtyMask(words, length, length);
    }

    /// <summary>
    /// Wraps words the caller has already filled. The caller promises never to
    /// write the words again and supplies the popcount it computed while filling.
    /// </summary>
    public static DirtyMask FromBits(ReadOnlyMemory<ulong> bits, int length, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, length);
        if (bits.Length < WordsFor(length))
        {
            throw new ArgumentException(
                $"{bits.Length} words cannot cover {length} bits.", nameof(bits));
        }

        return new DirtyMask(bits, length, count);
    }

    /// <summary>The set indices in ascending order.</summary>
    public IEnumerable<int> Indices()
    {
        for (int i = 0; i < Length; i++)
        {
            if (this[i])
            {
                yield return i;
            }
        }
    }

    /// <summary>How many 64-bit words cover <paramref name="length"/> bits.</summary>
    public static int WordsFor(int length) => (length + 63) >> 6;
}
