namespace Dse.Io;

/// <summary>
/// Queues writes into the simulation (spec 9.3). A write is applied at phase 1
/// of the next tick; it is never applied on the caller's thread. Implementations
/// reject an unknown tag, a kind mismatch and a read-only tag by throwing at
/// the call site, so a caller never queues something that cannot land.
/// </summary>
public interface ITagWriter
{
    /// <summary>The tag directory this writer validates against.</summary>
    ITagDirectory Directory { get; }

    /// <summary>Queues a value for the tag at an index.</summary>
    void Write(int index, TagValue value);

    /// <summary>Queues a value for a named tag.</summary>
    void Write(string name, TagValue value);
}
