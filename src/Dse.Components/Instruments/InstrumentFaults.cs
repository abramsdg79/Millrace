using Dse.Core.Faults;

namespace Dse.Components.Instruments;

/// <summary>The fault vocabulary every instrument supports.</summary>
public static class InstrumentFaults
{
    public const string Calibration = "calibration";
    public const string Noise = "noise";
    public const string Drift = "drift";
    public const string Lag = "lag";
    public const string Freeze = "freeze";
    public const string FailHigh = "fail-high";
    public const string FailLow = "fail-low";

    public static IReadOnlyList<FaultDescriptor> All { get; } =
    [
        new(Calibration, "Gain and offset error on the reading.",
            new FaultParameter("gain", "", 1.0, "Multiplier applied to the true value."),
            new FaultParameter("offset", "unit", 0.0, "Added after the gain.")),
        new(Noise, "Gaussian noise on the reading, replacing the datasheet noise.",
            new FaultParameter("sigma", "unit", 0.0, "Standard deviation per tick.")),
        new(Drift, "The reading walks away from the truth at a constant rate.",
            new FaultParameter("rate", "unit/s", 0.0, "Drift rate; clearing resets the accumulated drift.")),
        new(Lag, "A slower response than the datasheet.",
            new FaultParameter("seconds", "s", 0.0, "First-order time constant.")),
        new(Freeze, "The reading stops updating and holds its last value."),
        new(FailHigh, "The signal pins at the top of the range and health reads Bad."),
        new(FailLow, "The signal pins at the bottom of the range and health reads Bad."),
    ];
}
