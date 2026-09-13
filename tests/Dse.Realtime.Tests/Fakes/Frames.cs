using Dse.Io;

namespace Dse.Realtime.Tests.Fakes;

/// <summary>A three-tag directory and hand-built frames over it: A.Speed (double), A.Run (bool), B.Count (long).</summary>
public static class Frames
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    public const int Speed = 0;

    public const int Run = 1;

    public const int Count = 2;

    public static ArrayDirectory Directory() => new(
        new TagDescriptor(0, "A.Speed", TagKind.Double, TagAccess.ReadOnly, "m/s", 0.0, 2.0, "Belt speed"),
        new TagDescriptor(1, "A.Run", TagKind.Bool, TagAccess.ReadWrite, string.Empty, double.NaN, double.NaN, "Run command"),
        new TagDescriptor(2, "B.Count", TagKind.Int64, TagAccess.ReadOnly, "count", double.NaN, double.NaN, "Items"));

    public static DateTimeOffset TimeOf(long tick) => Start + (Step * tick);

    /// <summary>A frame with the given values; <paramref name="dirty"/> lists the changed indices.</summary>
    public static TickFrame Frame(long tick, double speed, bool run, long count, int[] dirty, params DiscreteEvent[] events)
    {
        TagValue[] values = [TagValue.Double(speed), TagValue.Bool(run), TagValue.Int64(count)];
        var words = new ulong[1];
        foreach (int i in dirty)
        {
            words[0] |= 1UL << i;
        }

        return new TickFrame(tick, TimeOf(tick), values, DirtyMask.FromBits(words, 3, dirty.Length), events);
    }

    /// <summary>A frame with every tag dirty.</summary>
    public static TickFrame Full(long tick, double speed, bool run, long count, params DiscreteEvent[] events) =>
        Frame(tick, speed, run, count, [0, 1, 2], events);

    /// <summary>A frame with nothing dirty and no events.</summary>
    public static TickFrame Quiet(long tick, double speed, bool run, long count) =>
        Frame(tick, speed, run, count, []);

    public static DiscreteEvent Event(long tick, string source, string code) =>
        new(tick, TimeOf(tick), source, code, $"{code} at tick {tick}.");
}

/// <summary>An <see cref="ITagDirectory"/> over an array.</summary>
public sealed class ArrayDirectory : ITagDirectory
{
    private readonly TagDescriptor[] _tags;
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    public ArrayDirectory(params TagDescriptor[] tags)
    {
        _tags = tags;
        for (int i = 0; i < tags.Length; i++)
        {
            _byName[tags[i].Name] = i;
        }
    }

    public int Count => _tags.Length;

    public IReadOnlyList<TagDescriptor> Tags => _tags;

    public TagDescriptor this[int index] => _tags[index];

    public bool TryFind(string name, out TagDescriptor descriptor)
    {
        if (_byName.TryGetValue(name, out int index))
        {
            descriptor = _tags[index];
            return true;
        }

        descriptor = null!;
        return false;
    }

    public TagDescriptor Find(string name) =>
        TryFind(name, out TagDescriptor d) ? d : throw new KeyNotFoundException($"No tag '{name}'.");
}
