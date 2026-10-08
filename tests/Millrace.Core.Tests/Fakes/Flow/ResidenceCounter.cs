using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>Accumulates, in one state slot, the seconds spent above a temperature.</summary>
public sealed class ResidenceCounter : IMaterialTransform
{
    private readonly double _thresholdTemperature;
    private readonly int _stateIndex;

    public ResidenceCounter(double thresholdTemperature, int stateIndex)
    {
        _thresholdTemperature = thresholdTemperature;
        _stateIndex = stateIndex;
    }

    public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context)
    {
        if (properties.Temperature > _thresholdTemperature)
        {
            state[_stateIndex] += dt;
        }
    }
}
