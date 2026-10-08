namespace Millrace.Io;

/// <summary>
/// A tag name resolved once to an index and a checked kind (spec 9.2). Reading
/// through a handle never pays for a dictionary lookup.
/// </summary>
/// <typeparam name="T">bool, double or long, matching the tag's kind.</typeparam>
public readonly struct TagHandle<T>
    where T : unmanaged
{
    /// <summary>Creates a handle. Implementations of <see cref="ITagReader.Handle{T}"/> call this after checking the kind.</summary>
    public TagHandle(int index, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Index = index;
        Name = name;
    }

    /// <summary>Position in the image.</summary>
    public int Index { get; }

    /// <summary>The tag's full name, kept for diagnostics.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
