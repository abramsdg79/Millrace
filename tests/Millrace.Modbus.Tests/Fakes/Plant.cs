using Millrace.Io;

namespace Millrace.Modbus.Tests.Fakes;

/// <summary>
/// A nine-tag directory with every kind and access, a claimed tag among them,
/// listed in the order a plant's directory would sort them, and the map it
/// gives:
/// coils A.Run 0, B.Stop 1; discrete inputs A.Permit 0, A.Running 1;
/// input registers A.Count 0–1, A.Speed 2–3;
/// holding registers A.Batch 0–1, A.Setpoint 2–3, B.Bias 4–5.
/// </summary>
public static class Plant
{
    public static ArrayDirectory Directory() => new(
        new TagDescriptor(0, "A.Batch", TagKind.Int64, TagAccess.ReadWrite, "count", double.NaN, double.NaN, "Batch number"),
        new TagDescriptor(1, "A.Count", TagKind.Int64, TagAccess.ReadOnly, "count", double.NaN, double.NaN, "Items counted"),
        new TagDescriptor(2, "A.Permit", TagKind.Bool, TagAccess.ReadOnly, "", double.NaN, double.NaN, "Run permit") { ClaimedBy = "INT_A" },
        new TagDescriptor(3, "A.Run", TagKind.Bool, TagAccess.ReadWrite, "", double.NaN, double.NaN, "Run command"),
        new TagDescriptor(4, "A.Running", TagKind.Bool, TagAccess.ReadOnly, "", double.NaN, double.NaN, "Running"),
        new TagDescriptor(5, "A.Setpoint", TagKind.Double, TagAccess.ReadWrite, "m/s", 0.0, 2.0, "Speed setpoint"),
        new TagDescriptor(6, "A.Speed", TagKind.Double, TagAccess.ReadOnly, "m/s", 0.0, 2.5, "Belt speed"),
        new TagDescriptor(7, "B.Bias", TagKind.Double, TagAccess.ReadWrite, "kg/s", double.NaN, double.NaN, "Unranged bias"),
        new TagDescriptor(8, "B.Stop", TagKind.Bool, TagAccess.ReadWrite, "", double.NaN, double.NaN, "Stop command"));

    /// <summary>An image in which every tag holds a recognisable value.</summary>
    public static TagValue[] Image() =>
    [
        TagValue.Int64(-7L),            // A.Batch
        TagValue.Int64(123_456L),       // A.Count
        TagValue.Bool(true),            // A.Permit
        TagValue.Bool(true),            // A.Run
        TagValue.Bool(false),           // A.Running
        TagValue.Double(1.5),           // A.Setpoint
        TagValue.Double(1.95),          // A.Speed
        TagValue.Double(-0.25),         // B.Bias
        TagValue.Bool(true),            // B.Stop
    ];
}

/// <summary>An <see cref="ITagDirectory"/> over an array, in the given order.</summary>
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

/// <summary>An <see cref="ITagWriter"/> that remembers every write, safe to share between connections.</summary>
public sealed class RecordingWriter(ITagDirectory directory) : ITagWriter
{
    private readonly Lock _sync = new();
    private readonly List<(string Tag, TagValue Value)> _writes = [];

    public ITagDirectory Directory { get; } = directory;

    /// <summary>A copy of the writes so far, in arrival order, by tag name.</summary>
    public (string Tag, TagValue Value)[] Writes
    {
        get
        {
            lock (_sync)
            {
                return [.. _writes];
            }
        }
    }

    public void Write(int index, TagValue value)
    {
        lock (_sync)
        {
            _writes.Add((Directory[index].Name, value));
        }
    }

    public void Write(string name, TagValue value) => Write(Directory.Find(name).Index, value);
}
