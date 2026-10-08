using Millrace.Io;

namespace Millrace.Realtime;

/// <summary>One tag's new value in a delta.</summary>
public readonly record struct TagChange(int Index, TagValue Value);
