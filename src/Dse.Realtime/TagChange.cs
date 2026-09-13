using Dse.Io;

namespace Dse.Realtime;

/// <summary>One tag's new value in a delta.</summary>
public readonly record struct TagChange(int Index, TagValue Value);
