using Dse.Io;

namespace Dse.Realtime;

/// <summary>One tag's current truth (spec 10.3): value with quality, and when it last changed.</summary>
public readonly record struct TagState(TagValue Value, long LastChangeTick, DateTimeOffset LastChangeTime);
