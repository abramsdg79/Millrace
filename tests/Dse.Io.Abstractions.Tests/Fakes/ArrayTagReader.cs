using Dse.Io;

namespace Dse.Io.Abstractions.Tests.Fakes;

/// <summary>A reader and writer over two arrays — the smallest possible implementation of the contract.</summary>
public sealed class ArrayTagReader : ITagReader, ITagWriter, ITagDirectory
{
    private readonly TagDescriptor[] _tags;
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    public ArrayTagReader(params TagDescriptor[] tags)
    {
        _tags = tags;
        Values = new TagValue[tags.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            _byName[tags[i].Name] = i;
            Values[i] = tags[i].Kind switch
            {
                TagKind.Bool => TagValue.Bool(false),
                TagKind.Double => TagValue.Double(0.0),
                _ => TagValue.Int64(0L),
            };
        }
    }

    public TagValue[] Values { get; }

    public List<(int Index, TagValue Value)> Writes { get; } = [];

    public ITagDirectory Directory => this;

    public long SnapshotTick { get; set; }

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

    public TagValue Read(int index) => Values[index];

    public TagValue Read(string name) => Values[Find(name).Index];

    public TagHandle<T> Handle<T>(string name)
        where T : unmanaged
    {
        TagDescriptor d = Find(name);
        if (d.Kind != TagValue.KindOf<T>())
        {
            throw new InvalidOperationException($"'{name}' is a {d.Kind} tag, not {typeof(T).Name}.");
        }

        return new TagHandle<T>(d.Index, d.Name);
    }

    public void Write(int index, TagValue value) => Writes.Add((index, value));

    public void Write(string name, TagValue value) => Write(Find(name).Index, value);
}
