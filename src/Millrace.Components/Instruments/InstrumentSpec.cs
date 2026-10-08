namespace Millrace.Components.Instruments;

/// <summary>What an instrument's datasheet says.</summary>
/// <param name="Unit">Engineering unit of <c>Value</c>.</param>
/// <param name="RangeLow">Lowest value the transmitter can report.</param>
/// <param name="RangeHigh">Highest value the transmitter can report.</param>
/// <param name="NoiseSigma">Standard deviation of per-tick Gaussian noise, in the unit. Zero for none.</param>
/// <param name="LagSeconds">First-order time constant of the reading. Zero for instantaneous.</param>
public readonly record struct InstrumentSpec(
    string Unit,
    double RangeLow,
    double RangeHigh,
    double NoiseSigma = 0.0,
    double LagSeconds = 0.0);
