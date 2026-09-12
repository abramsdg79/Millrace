using Dse.Core.Flow;

namespace Dse.Components.Transforms;

/// <summary>
/// Moisture leaves at a rate proportional to how far the material is above a
/// threshold temperature. Changes the moisture fraction only: transforms have
/// no mass authority, so the evaporated water stays in the mass ledger. A
/// dryer that must lose mass is a process unit with a yield below one.
/// </summary>
public sealed class MoistureLoss : IMaterialTransform
{
    public MoistureLoss(double ratePerDegreeSecond, double thresholdTemperature)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ratePerDegreeSecond);
        RatePerDegreeSecond = ratePerDegreeSecond;
        ThresholdTemperature = thresholdTemperature;
    }

    /// <summary>Moisture fraction lost per °C above the threshold per second.</summary>
    public double RatePerDegreeSecond { get; }

    /// <summary>°C.</summary>
    public double ThresholdTemperature { get; }

    public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context)
    {
        double excess = properties.Temperature - ThresholdTemperature;
        if (excess <= 0.0)
        {
            return;
        }

        double moisture = Math.Max(0.0, properties.Moisture - (RatePerDegreeSecond * excess * dt));
        properties = properties with { Moisture = moisture };
    }
}
