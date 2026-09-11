namespace Dse.Core.Time;

/// <summary>The complete set of inputs that determine a simulation's results.</summary>
public sealed class SimulationOptions
{
    /// <summary>Master seed. Every component's stream is derived from this.</summary>
    public ulong Seed { get; init; }

    public DateTimeOffset StartTime { get; init; } =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public TimeSpan TimeStep { get; init; } = TimeSpan.FromMilliseconds(10);
}
