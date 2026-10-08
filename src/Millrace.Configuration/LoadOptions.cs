namespace Millrace.Configuration;

/// <summary>Overrides for a plant's <c>defaults</c> block. A scenario or a command line sets these.</summary>
public sealed class LoadOptions
{
    public TimeSpan? TimeStep { get; init; }

    public ulong? Seed { get; init; }

    public DateTimeOffset? StartTime { get; init; }
}
