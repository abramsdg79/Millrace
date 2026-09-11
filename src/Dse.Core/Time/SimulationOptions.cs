namespace Dse.Core.Time;

/// <summary>The complete set of inputs that determine a simulation's results.</summary>
public sealed class SimulationOptions
{
    /// <summary>Master seed. Every component's stream is derived from this.</summary>
    public ulong Seed { get; init; }

    public DateTimeOffset StartTime { get; init; } =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public TimeSpan TimeStep { get; init; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Audit sourced − sunk − held after every flow phase and throw on drift.
    /// On by default: the check is O(nodes) and a violation is a bug worth
    /// stopping for. Switch it off per run for throughput, never silently.
    /// </summary>
    public bool CheckConservation { get; init; } = true;

    /// <summary>Allowed drift, relative to max(1 kg, total mass sourced).</summary>
    public double ConservationTolerance { get; init; } = 1e-9;
}
