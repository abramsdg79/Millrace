namespace Millrace.Io;

/// <summary>
/// Reads the published I/O image (spec 9.3). Every read sees the consistent
/// snapshot published at the most recent tick's phase 4; reads may be issued
/// from any thread and never block the simulation.
/// </summary>
public interface ITagReader
{
    /// <summary>The tag directory this reader indexes.</summary>
    ITagDirectory Directory { get; }

    /// <summary>The tick whose image is currently visible, or -1 before the first tick.</summary>
    long SnapshotTick { get; }

    /// <summary>The value at an index in the current snapshot.</summary>
    TagValue Read(int index);

    /// <summary>The value of a named tag in the current snapshot. Resolves the name every call; prefer a handle in per-scan code.</summary>
    TagValue Read(string name);

    /// <summary>
    /// Resolves a name once to a typed handle. Throws <see cref="KeyNotFoundException"/>
    /// for an unknown tag and <see cref="InvalidOperationException"/> when
    /// <typeparamref name="T"/> does not match the tag's kind.
    /// </summary>
    TagHandle<T> Handle<T>(string name)
        where T : unmanaged;
}
