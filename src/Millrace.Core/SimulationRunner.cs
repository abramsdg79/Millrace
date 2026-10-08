using System.Diagnostics;

namespace Millrace.Core;

public enum ExecutionMode
{
    /// <summary>No pacing. An eight-hour scenario finishes in seconds.</summary>
    AsFastAsPossible,

    /// <summary>One second of simulation per second of wall clock.</summary>
    RealTime,

    /// <summary>Real time multiplied by a speed factor.</summary>
    Scaled,
}

/// <summary>
/// Drives a simulation. All pacing lives here, so switching execution mode cannot
/// change results — only how long the run takes.
/// </summary>
public sealed class SimulationRunner
{
    private readonly Simulation _simulation;
    private readonly ExecutionMode _mode;
    private readonly double _speedFactor;

    public SimulationRunner(
        Simulation simulation,
        ExecutionMode mode = ExecutionMode.AsFastAsPossible,
        double speedFactor = 1.0)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (speedFactor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speedFactor), speedFactor, "The speed factor must be positive.");
        }

        _simulation = simulation;
        _mode = mode;
        _speedFactor = mode == ExecutionMode.RealTime ? 1.0 : speedFactor;
    }

    /// <summary>Advances by a fixed number of ticks, ignoring pacing. Use for step-by-step control.</summary>
    public void Step(int ticks = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        for (int i = 0; i < ticks; i++)
        {
            _simulation.Tick();
        }
    }

    public void RunFor(TimeSpan simDuration, CancellationToken cancellationToken = default)
    {
        long stepTicks = _simulation.Clock.TimeStep.Ticks;
        long tickCount = simDuration.Ticks / stepTicks;
        bool paced = _mode is ExecutionMode.RealTime or ExecutionMode.Scaled;
        double stepMilliseconds = _simulation.Clock.TimeStep.TotalMilliseconds / _speedFactor;

        Stopwatch? stopwatch = paced ? Stopwatch.StartNew() : null;

        for (long i = 0; i < tickCount; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _simulation.Tick();

            if (stopwatch is null)
            {
                continue;
            }

            double targetMilliseconds = (i + 1) * stepMilliseconds;
            double waitMilliseconds = targetMilliseconds - stopwatch.Elapsed.TotalMilliseconds;
            if (waitMilliseconds >= 1.0)
            {
                cancellationToken.WaitHandle.WaitOne((int)waitMilliseconds);
            }
        }
    }
}
