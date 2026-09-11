namespace Dse.Core.Time;

/// <summary>
/// Fixed-step simulation clock. Time is derived from the tick count by
/// multiplication and never accumulated, because accumulating a step drifts and
/// drift breaks replay.
/// </summary>
public sealed class SimulationClock
{
    public SimulationClock(DateTimeOffset startTime, TimeSpan timeStep)
    {
        if (timeStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeStep), timeStep, "The time step must be positive.");
        }

        StartTime = startTime;
        TimeStep = timeStep;
        DeltaSeconds = timeStep.TotalSeconds;
    }

    public DateTimeOffset StartTime { get; }

    public TimeSpan TimeStep { get; }

    /// <summary>The time step in seconds, precomputed for use inside Evaluate.</summary>
    public double DeltaSeconds { get; }

    public long TickCount { get; private set; }

    public TimeSpan Elapsed => TimeSpan.FromTicks(TimeStep.Ticks * TickCount);

    public DateTimeOffset Now => StartTime + Elapsed;

    public void Advance() => TickCount++;
}
