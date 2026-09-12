using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>
/// A speed switch: <see cref="Stopped"/> goes true once the *reading* has
/// been below the threshold for the delay, and false the moment it is not.
/// Derived from the faulted reading, so a frozen switch never notices a stop.
/// </summary>
public sealed class ZeroSpeedSwitch : InstrumentBase
{
    private double _belowFor;
    private bool _stopped;

    public ZeroSpeedSwitch(string id, InstrumentSpec spec, double thresholdSpeed, double delaySeconds)
        : base(id, spec)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(thresholdSpeed);
        ArgumentOutOfRangeException.ThrowIfNegative(delaySeconds);
        ThresholdSpeed = thresholdSpeed;
        DelaySeconds = delaySeconds;

        Speed = AddInput<double>("Speed");
        Stopped = AddOutput<bool>("Stopped");
    }

    /// <summary>True speed, m/s.</summary>
    public InputPort<double> Speed { get; }

    public OutputPort<bool> Stopped { get; }

    /// <summary>m/s.</summary>
    public double ThresholdSpeed { get; }

    /// <summary>s the reading must stay below the threshold.</summary>
    public double DelaySeconds { get; }

    protected override double Measure(in TickContext ctx) => Speed.Value;

    protected override void OnEvaluated(double reading, in TickContext ctx)
    {
        bool below = reading < ThresholdSpeed;
        _belowFor = below ? _belowFor + ctx.Dt : 0.0;
        bool stopped = below && _belowFor >= DelaySeconds - 1e-12;

        if (stopped != _stopped)
        {
            ctx.Log(
                Id,
                stopped ? "ZERO_SPEED" : "MOTION",
                stopped
                    ? string.Create(CultureInfo.InvariantCulture, $"Speed below {ThresholdSpeed} m/s for {DelaySeconds} s.")
                    : "Motion detected.");
        }

        _stopped = stopped;
        Stopped.Value = stopped;
    }
}
