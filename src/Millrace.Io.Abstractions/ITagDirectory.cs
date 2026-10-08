namespace Millrace.Io;

/// <summary>
/// The printable list of tags (spec 9.1). Ordered by index; names are unique
/// and compared ordinally. Immutable for the life of a simulation.
/// </summary>
public interface ITagDirectory
{
    /// <summary>Number of tags.</summary>
    int Count { get; }

    /// <summary>All descriptors, in index order.</summary>
    IReadOnlyList<TagDescriptor> Tags { get; }

    /// <summary>The descriptor at an index.</summary>
    TagDescriptor this[int index] { get; }

    /// <summary>Looks a tag up by name.</summary>
    bool TryFind(string name, out TagDescriptor descriptor);

    /// <summary>Looks a tag up by name; throws <see cref="KeyNotFoundException"/> if absent.</summary>
    TagDescriptor Find(string name);
}
