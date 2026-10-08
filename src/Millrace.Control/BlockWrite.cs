using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// A tag a block commands and the value it commands on it: an interlock's trip
/// write, a sequencer's step entry write, an abort write.
/// </summary>
/// <param name="Tag">The full name of a read-write tag.</param>
/// <param name="Value">The value to command. Its kind must match the tag's.</param>
public sealed record BlockWrite(string Tag, TagValue Value);
